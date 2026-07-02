# Plan 011: Criar índices do caminho quente (conversations.contact_phone e subscriptions)

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `plans/README.md`.
>
> **Drift check (run first)**: `git diff --stat f809720..HEAD -- src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs src/Atendefy.API/Infrastructure/Database/TenantSchemaMigrator.cs src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs src/Atendefy.API/Infrastructure/Database/PublicDbContext.cs`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: S
- **Risk**: LOW
- **Depends on**: none
- **Category**: perf
- **Planned at**: commit `f809720`, 2026-07-02

## Why this matters

Toda mensagem WhatsApp recebida dispara pelo menos duas queries
`WHERE contact_phone = @phone` na tabela `conversations` do schema do tenant
(check de BotPaused no `ConversationWorker` e get-or-create no
`ConversationService`) — e **não existe índice em `contact_phone`**: é table
scan, com custo crescendo linearmente com o histórico de conversas do tenant.
No schema `public`, `BillingService` filtra `Subscriptions` por
`(TenantId, Status)` sem índice composto. Este plano cria os índices nos três
lugares onde o schema de tenant é definido (provisioner para tenants novos,
patch do migrator para tenants existentes, modelo EF para documentação) e uma
migração EF para o schema public.

## Current state

**Arquitetura de schema (importante!)**: o schema `public` usa migrações EF
(`src/Atendefy.API/Infrastructure/Database/Migrations/`), aplicadas no startup.
Os schemas de tenant NÃO usam EF migrations: são criados por SQL cru no
`TenantProvisioner` (tenants novos) e atualizados pelo `TenantSchemaMigrator`
(patch idempotente com skip por hash — ao mudar o `PatchSqlTemplate`, o hash
muda e o patch reaplica em todos os tenants no próximo boot; é esse o mecanismo
para levar o índice aos tenants existentes).

Arquivos e excerpts:

1. `src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs:71-81` — DDL da tabela
   conversations (sem índice). O padrão de índice já usado no mesmo arquivo
   (linhas 68–69):

```sql
CREATE INDEX IF NOT EXISTS ix_appointments_external_id
    ON "{schemaName}".appointments (external_id);

CREATE TABLE IF NOT EXISTS "{schemaName}".conversations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    contact_phone VARCHAR(30) NOT NULL,
    ...
);
```

2. `src/Atendefy.API/Infrastructure/Database/TenantSchemaMigrator.cs:19-42` —
   const `PatchSqlTemplate` com `{0}` = schema name; contém ALTERs idempotentes e
   `CREATE TABLE IF NOT EXISTS` para contacts/quick_replies. Comentário no topo do
   template avisa: "Ao alterar este template, o hash muda e o patch reaplica em
   todos os tenants."

3. `src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs:61-72` — config da
   entidade Conversation (sem HasIndex em ContactPhone):

```csharp
modelBuilder.Entity<Conversation>(e =>
{
    e.ToTable("conversations");
    e.HasKey(x => x.Id);
    e.Property(x => x.ContactPhone).HasMaxLength(30).IsRequired();
    ...
});
```

4. `src/Atendefy.API/Infrastructure/Database/PublicDbContext.cs:64-75` — config de
   Subscription (sem índice em TenantId/Status). Query que motiva o índice:
   `src/Atendefy.API/Modules/Billing/BillingService.cs:129-130`
   (`FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Status != ...Cancelled)`).

Queries do hot path que o índice de tenant atende:
- `src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs:117-118`
- `src/Atendefy.API/Modules/Chatbot/ConversationService.cs:50-51`

Ferramenta: `dotnet ef` **10.0.9** está instalado globalmente na máquina de dev
(`dotnet ef --version`). O pacote `Microsoft.EntityFrameworkCore.Design` 10.0.9
já está no csproj.

Nota vetada: `webhook_routes.LookupKey` JÁ tem índice único
(`PublicDbContext.cs:50`) — um composto (Provider, LookupKey) foi considerado e
rejeitado (ganho marginal). Não criar.

## Commands you will need

| Purpose  | Command | Expected on success |
|----------|---------|---------------------|
| Build    | `dotnet build Atendefy.slnx -c Release` | exit 0 |
| Testes   | `dotnet test Atendefy.slnx -c Release`  | todos passam |
| Migração | `dotnet ef migrations add AddSubscriptionTenantStatusIndex --project src/Atendefy.API` | 3 arquivos criados em Infrastructure/Database/Migrations |

## Scope

**In scope**:
- `src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs` (adicionar 1 índice ao DDL)
- `src/Atendefy.API/Infrastructure/Database/TenantSchemaMigrator.cs` (adicionar o mesmo índice ao `PatchSqlTemplate`)
- `src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs` (HasIndex documentacional)
- `src/Atendefy.API/Infrastructure/Database/PublicDbContext.cs` (HasIndex em Subscription)
- `src/Atendefy.API/Infrastructure/Database/Migrations/*` (nova migração gerada)

**Out of scope**:
- Refatorar o número de DbContexts criados por mensagem no worker (finding
  separado, não selecionado nesta rodada).
- Qualquer outro índice (appointments, webhook_routes, contacts) — já existem ou foram rejeitados.
- As queries em si — não mudar código de consulta.

## Git workflow

- Branch: `advisor/011-hot-path-indexes`
- Conventional commits em português (ex.: `perf(db): indexar conversations.contact_phone e subscriptions(tenant_id, status)`)
- Não fazer push nem abrir PR sem instrução do operador.

## Steps

### Step 1: Índice no provisioner (tenants novos)

Em `TenantProvisioner.cs`, logo APÓS o `CREATE TABLE ... conversations (...)`
(depois da linha 81), adicionar no mesmo padrão do índice de appointments:

```sql
CREATE INDEX IF NOT EXISTS ix_conversations_contact_phone
    ON "{schemaName}".conversations (contact_phone);
```

**Verify**: `dotnet build Atendefy.slnx -c Release` → exit 0.

### Step 2: Índice no patch do migrator (tenants existentes)

Em `TenantSchemaMigrator.cs`, adicionar ao FINAL do `PatchSqlTemplate` (antes do
fechamento `"""`), usando `{0}` como placeholder de schema:

```sql
CREATE INDEX IF NOT EXISTS ix_conversations_contact_phone
    ON "{0}".conversations (contact_phone);
```

Isso muda o hash do template → no próximo boot o patch reaplica em todos os
tenants (comportamento desejado; o índice é `IF NOT EXISTS`, idempotente).

**Verify**: `dotnet build Atendefy.slnx -c Release` → exit 0.

### Step 3: HasIndex no modelo EF (documentação do modelo de tenant)

Em `TenantDbContext.cs`, dentro do bloco `Entity<Conversation>` (linhas 61–72),
adicionar após a linha do `ContactPhone`:

```csharp
e.HasIndex(x => x.ContactPhone);
```

(Sem efeito em runtime para Postgres — o schema de tenant é SQL cru — mas mantém
o modelo fiel e vale para o provider InMemory dos testes.)

**Verify**: `dotnet build Atendefy.slnx -c Release` → exit 0.

### Step 4: Índice composto em Subscription (schema public) + migração EF

1. Em `PublicDbContext.cs`, dentro do bloco `Entity<Subscription>` (linhas 64–75),
   adicionar:

```csharp
e.HasIndex(x => new { x.TenantId, x.Status });
```

2. Gerar a migração:

```
dotnet ef migrations add AddSubscriptionTenantStatusIndex --project src/Atendefy.API
```

3. Inspecionar o arquivo gerado em `Infrastructure/Database/Migrations/`: o `Up`
   deve conter apenas `CreateIndex` em `subscriptions` com colunas
   `tenant_id, status` (o repo usa EFCore.NamingConventions snake_case). Se a
   migração contiver QUALQUER outra operação (alter/drop de outras tabelas),
   é sinal de drift de modelo → STOP condition.

A migração é aplicada automaticamente no startup (`Program.cs` roda
`MigrateAsync()` no boot, fora do ambiente Testing).

**Verify**: `dotnet build Atendefy.slnx -c Release` → exit 0; a migração nova
existe e contém somente o CreateIndex esperado.

### Step 5: Suíte completa

**Verify**: `dotnet test Atendefy.slnx -c Release` → todos passam (InMemory
ignora índices; nada deve quebrar).

## Test plan

- Sem testes novos: índices não têm comportamento observável em InMemory.
- Gate: suíte completa verde + inspeção manual da migração gerada (Step 4.3).
- (Pós-deploy, fora do plano) validar com
  `EXPLAIN SELECT ... FROM "tenant_xxx".conversations WHERE contact_phone='...'`
  que o plano usa `ix_conversations_contact_phone`.

## Done criteria

- [ ] `dotnet build Atendefy.slnx -c Release` → exit 0
- [ ] `dotnet test Atendefy.slnx -c Release` → exit 0
- [ ] `grep -n "ix_conversations_contact_phone" src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs` → 1 match
- [ ] `grep -n "ix_conversations_contact_phone" src/Atendefy.API/Infrastructure/Database/TenantSchemaMigrator.cs` → 1 match
- [ ] `grep -rn "AddSubscriptionTenantStatusIndex" src/Atendefy.API/Infrastructure/Database/Migrations/` → 2+ matches (migração + designer)
- [ ] `git status` — só arquivos in-scope modificados/criados
- [ ] Linha do plano 011 atualizada em `plans/README.md`

## STOP conditions

- Excerpts não batem com o código (drift).
- A migração gerada no Step 4 contém operações além do CreateIndex de
  subscriptions (drift entre modelo EF e snapshot — investigar antes de commitar).
- `dotnet ef` não estiver disponível e a instalação falhar
  (`dotnet tool install -g dotnet-ef` é aceitável tentar UMA vez).

## Maintenance notes

- O hash do `PatchSqlTemplate` mudou: o primeiro boot após o deploy reaplica o
  patch em todos os tenants (segundos por tenant, uma vez). Esperado.
- Se um dia a lista de conversas ganhar filtro por `is_resolved`, considerar
  índice composto `(contact_phone, is_resolved)` no lugar — revisar na ocasião.
- Revisor: conferir que o índice em provisioner e migrator têm o MESMO nome
  (`ix_conversations_contact_phone`) para o `IF NOT EXISTS` deduplicar.
