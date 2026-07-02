# Plan 010: Tornar a deduplicação de webhooks atômica (SET NX)

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `plans/README.md`.
>
> **Drift check (run first)**: `git diff --stat f809720..HEAD -- src/Atendefy.API/Modules/Webhooks/WebhookEndpoints.cs src/Atendefy.API/Infrastructure/Cache/RedisService.cs tests/Atendefy.Tests/Integration/ApiFactory.cs`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: S
- **Risk**: LOW
- **Depends on**: none
- **Category**: bug
- **Planned at**: commit `f809720`, 2026-07-02

## Why this matters

A deduplicação de webhooks (Meta reenvia webhooks em retry; Evolution reenvia
após restart) usa check-then-set em duas chamadas Redis separadas: `EXISTS` e
depois `SET`. Duas requisições concorrentes com o mesmo `messageId` podem ambas
ver a chave ausente e ambas passar — a mensagem é processada duas vezes, o bot
responde duas vezes e o cliente é contabilizado duas vezes no teto mensal.
O Redis tem operação atômica para isso: `SET key value EX ttl NX` em uma chamada.

## Current state

Arquivos relevantes:

- `src/Atendefy.API/Modules/Webhooks/WebhookEndpoints.cs` — endpoints `/webhooks/meta` e `/webhooks/evolution`; helper `IsDuplicateAsync` nas linhas 248–255
- `src/Atendefy.API/Infrastructure/Cache/RedisService.cs` — wrapper de Redis usado pelo helper
- `tests/Atendefy.Tests/Integration/ApiFactory.cs` — mocka `IConnectionMultiplexer` (linhas 78–86); o stub de `StringSetAsync` precisa cobrir o overload novo

`WebhookEndpoints.cs:248-255` (hoje — o bug):

```csharp
private static async Task<bool> IsDuplicateAsync(
    RedisService redis, string provider, string messageId)
{
    var key = $"webhook:dedup:{provider}:{messageId}";
    if (await redis.ExistsAsync(key)) return true;
    await redis.SetAsync(key, "1", TimeSpan.FromHours(24));
    return false;
}
```

Call sites (não mudam): `WebhookEndpoints.cs:69-71` (meta) e `:116-118` (evolution).

`RedisService.cs` (métodos vizinhos, para seguir o estilo):

```csharp
public async Task SetAsync(string key, string value, TimeSpan? expiry = null)
    => await _db.StringSetAsync(key, value, expiry);

public async Task<bool> ExistsAsync(string key)
    => await _db.KeyExistsAsync(key);
```

`ApiFactory.cs:78-83` (mock atual de Redis nos testes de integração):

```csharp
var mockConn = Substitute.For<IConnectionMultiplexer>();
var mockDb = Substitute.For<IDatabase>();
mockDb.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>()).Returns(1L);
mockDb.StringGetAsync(Arg.Any<RedisKey>()).Returns(new RedisValue());
mockDb.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
    Arg.Any<When>(), Arg.Any<CommandFlags>()).Returns(true);
mockConn.GetDatabase().Returns(mockDb);
```

Convenções: chaves Redis `"entidade:identificador"`; testes unitários com
NSubstitute — exemplar: `tests/Atendefy.Tests/Infrastructure/RedisServiceTests.cs`.

## Commands you will need

| Purpose | Command                                 | Expected on success |
|---------|-----------------------------------------|---------------------|
| Build   | `dotnet build Atendefy.slnx -c Release` | exit 0              |
| Testes  | `dotnet test Atendefy.slnx -c Release`  | todos passam        |

## Scope

**In scope**:
- `src/Atendefy.API/Infrastructure/Cache/RedisService.cs` (adicionar 1 método)
- `src/Atendefy.API/Modules/Webhooks/WebhookEndpoints.cs` (somente `IsDuplicateAsync`)
- `tests/Atendefy.Tests/Infrastructure/RedisServiceTests.cs` (adicionar casos)
- `tests/Atendefy.Tests/Integration/ApiFactory.cs` (apenas se o stub do mock precisar do overload — ver Step 3)

**Out of scope**:
- Os handlers dos webhooks em si (parsing, roteamento, publish no stream).
- TTL de 24h e formato da chave — manter idênticos.

## Git workflow

- Branch: `advisor/010-webhook-dedup-atomic`
- Conventional commits em português (ex. no histórico: `fix(webhooks): deduplicar mensagens Meta e Evolution via Redis`)
- Não fazer push nem abrir PR sem instrução do operador.

## Steps

### Step 1: Adicionar `TrySetIfAbsentAsync` ao RedisService

Em `RedisService.cs`, junto aos métodos existentes:

```csharp
/// <summary>
/// SET key value EX ttl NX — atômico. Retorna true se a chave foi criada
/// (não existia), false se já existia. Usado para deduplicação.
/// </summary>
public async Task<bool> TrySetIfAbsentAsync(string key, string value, TimeSpan ttl)
    => await _db.StringSetAsync(key, value, ttl, When.NotExists);
```

**Verify**: `dotnet build Atendefy.slnx -c Release` → exit 0.

### Step 2: Usar a operação atômica no helper de dedup

Em `WebhookEndpoints.cs:248-255`, substituir o corpo:

```csharp
private static async Task<bool> IsDuplicateAsync(
    RedisService redis, string provider, string messageId)
{
    var key = $"webhook:dedup:{provider}:{messageId}";
    // SET NX atômico: elimina a janela check-then-set entre requisições concorrentes.
    return !await redis.TrySetIfAbsentAsync(key, "1", TimeSpan.FromHours(24));
}
```

**Verify**: `dotnet build Atendefy.slnx -c Release` → exit 0.

### Step 3: Garantir que o mock do ApiFactory cobre o overload

O overload `StringSetAsync(key, value, expiry, When)` usado pelo novo método pode
não bater com o stub existente (que inclui `CommandFlags`). Rode a suíte; se algum
teste de integração falhar com dedup inesperado (NSubstitute retorna `false` por
default → `IsDuplicate` retornaria `true`), adicione ao `ApiFactory.cs`, junto ao
stub existente:

```csharp
mockDb.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
    Arg.Any<When>()).Returns(true);
```

**Verify**: `dotnet test Atendefy.slnx -c Release` → todos passam.

### Step 4: Testes unitários do novo método

Em `tests/Atendefy.Tests/Infrastructure/RedisServiceTests.cs`, adicionar (seguindo
o padrão do arquivo):

1. `TrySetIfAbsentAsync_WhenKeyIsNew_ReturnsTrue` — stub `StringSetAsync(..., When.NotExists)` → `true`; assert `true` e `Received()` com `When.NotExists` e o TTL passado.
2. `TrySetIfAbsentAsync_WhenKeyExists_ReturnsFalse` — stub → `false`; assert `false`.

**Verify**: `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~TrySetIfAbsent"` → 2 passam.

## Test plan

- 2 testes unitários novos (Step 4) em `RedisServiceTests.cs`.
- Suíte completa verde: `dotnet test Atendefy.slnx -c Release`.

## Done criteria

- [ ] `dotnet build Atendefy.slnx -c Release` → exit 0
- [ ] `dotnet test Atendefy.slnx -c Release` → exit 0, incluindo os 2 novos testes
- [ ] `grep -n "ExistsAsync" src/Atendefy.API/Modules/Webhooks/WebhookEndpoints.cs` → nenhum match (o check-then-set sumiu)
- [ ] `grep -n "TrySetIfAbsentAsync" src/Atendefy.API/Modules/Webhooks/WebhookEndpoints.cs` → 1 match
- [ ] `git status` — só arquivos in-scope modificados
- [ ] Linha do plano 010 atualizada em `plans/README.md`

## STOP conditions

- Excerpts de "Current state" não batem com o código (drift).
- O overload `StringSetAsync(key, value, ttl, When.NotExists)` não existir no
  StackExchange.Redis 2.8.0 (não fazer upgrade do pacote; reporte).
- Mais de 2 testes de integração falharem após o Step 3 mesmo com o stub novo.

## Maintenance notes

- Se o plano 009 (stream reliability) introduzir retries, a chave de dedup de 24h
  continua correta: o retry acontece no consumo, não no webhook.
- `RedisService.ExistsAsync` continua usado? Verificar com grep; se este era o único
  uso, o método pode ficar (API pública do wrapper) — não remover neste plano.
