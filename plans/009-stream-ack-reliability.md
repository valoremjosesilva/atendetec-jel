# Plan 009: Não perder mensagens quando o processamento falha no ConversationWorker

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `plans/README.md`.
>
> **Drift check (run first)**: `git diff --stat f809720..HEAD -- src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs src/Atendefy.API/Infrastructure/Messaging/RedisStreamService.cs`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED
- **Depends on**: none
- **Category**: bug
- **Planned at**: commit `f809720`, 2026-07-02

## Why this matters

O `ConversationWorker` consome o Redis Stream `messages.inbound` (toda mensagem
de WhatsApp recebida passa por aqui). Hoje, se `ProcessEntryAsync` lança exceção
(erro transitório de banco, timeout do provider de IA, chave corrompida), o ACK
nunca acontece e a entrada fica **presa na Pending Entries List (PEL) do consumer
group para sempre** — o `ReadGroupAsync` só lê entradas novas (`">"`) e não há
`XAUTOCLAIM`. Pior: o `try/catch` fica FORA do `foreach`, então uma exceção
aborta o batch inteiro e as demais entradas já entregues do mesmo batch também
ficam presas. Resultado: **mensagens de clientes silenciosamente perdidas**.
Este plano adiciona: (1) isolamento de falha por entrada, (2) redelivery de
entradas pendentes via `XAUTOCLAIM`, (3) dead-letter após N tentativas.

## Current state

Arquivos relevantes:

- `src/Atendefy.API/Infrastructure/Messaging/RedisStreamService.cs` — wrapper fino de streams (33 linhas, arquivo inteiro abaixo)
- `src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs` — BackgroundService consumidor; loop em `ExecuteAsync` (linhas 38–67)

`RedisStreamService.cs` (arquivo completo hoje):

```csharp
public class RedisStreamService(IConnectionMultiplexer connection)
{
    private readonly IDatabase _db = connection.GetDatabase();

    public async Task PublishAsync(string stream, Dictionary<string, string> fields)
    {
        var entries = fields.Select(f => new NameValueEntry(f.Key, f.Value)).ToArray();
        await _db.StreamAddAsync(stream, entries);
    }

    public async Task<StreamEntry[]> ReadGroupAsync(string stream, string group, string consumer, int count = 10)
        => await _db.StreamReadGroupAsync(stream, group, consumer, ">", count);

    public async Task AcknowledgeAsync(string stream, string group, RedisValue messageId)
        => await _db.StreamAcknowledgeAsync(stream, group, messageId);

    public async Task EnsureConsumerGroupAsync(string stream, string group)
    {
        try
        {
            await _db.StreamCreateConsumerGroupAsync(stream, group, StreamPosition.Beginning);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // grupo já existe — esperado
        }
    }
}
```

`ConversationWorker.cs:38-67` (loop atual — note o ACK sem try/catch por entrada
e o catch externo que aborta o batch):

```csharp
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
```

Constantes existentes no worker (linhas 34–36): `StreamName = "messages.inbound"`,
`GroupName = "conversation-workers"`, `ConsumerName = "worker-1"`.

Convenções do repo que se aplicam:
- Serviços com primary constructor; logs via `ILogger` estruturado (`logger.LogWarning("... {TenantId}", id)`).
- Chaves Redis no formato `"entidade:identificador"` (ex.: `"aiconfig:{schema}"`).
- Pacote StackExchange.Redis **2.8.0** — a API `StreamAutoClaimAsync` existe nessa versão.
- Testes unitários instanciam a classe direto com NSubstitute; ver `tests/Atendefy.Tests/Infrastructure/RedisServiceTests.cs` como exemplar de mock de `IConnectionMultiplexer`/`IDatabase`.

## Commands you will need

| Purpose   | Command                                              | Expected on success |
|-----------|------------------------------------------------------|---------------------|
| Build     | `dotnet build Atendefy.slnx -c Release`              | exit 0, 0 erros     |
| Testes    | `dotnet test Atendefy.slnx -c Release`               | todos passam (117+ hoje) |
| Teste único | `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~NomeDoTeste"` | passa |

## Scope

**In scope** (únicos arquivos a modificar):
- `src/Atendefy.API/Infrastructure/Messaging/RedisStreamService.cs`
- `src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs` (apenas `ExecuteAsync` e novos métodos privados/campos)
- `tests/Atendefy.Tests/Infrastructure/RedisStreamServiceTests.cs` (criar)

**Out of scope** (NÃO tocar):
- `ProcessEntryAsync` e demais métodos de negócio do worker — a lógica de
  processamento não muda, só o envelope de confiabilidade.
- `WebhookEndpoints.cs` (produtor) — formato das entradas não muda.
- `ApiFactory.cs` dos testes — workers já são removidos em Testing.

## Git workflow

- Branch: `advisor/009-stream-ack-reliability`
- Commits em português, conventional commits (exemplos no repo: `fix(webhooks): deduplicar mensagens...`, `perf(chatbot): usar cache...`)
- Não fazer push nem abrir PR sem instrução do operador.

## Steps

### Step 1: Adicionar suporte a autoclaim e dead-letter no RedisStreamService

Em `RedisStreamService.cs`, adicionar dois métodos:

```csharp
/// <summary>
/// Reivindica entradas pendentes (entregues mas não ACKadas) paradas há mais de
/// <paramref name="minIdle"/> — inclusive as deste próprio consumer (crash/exceção).
/// Retorna também o delivery count de cada uma para decidir dead-letter.
/// </summary>
public async Task<StreamEntry[]> ClaimPendingAsync(
    string stream, string group, string consumer, TimeSpan minIdle, int count = 10)
{
    var result = await _db.StreamAutoClaimAsync(
        stream, group, consumer, (long)minIdle.TotalMilliseconds, "0-0", count);
    return result.ClaimedEntries;
}

/// <summary>Delivery count de uma entrada pendente (XPENDING). 0 se não estiver pendente.</summary>
public async Task<long> GetDeliveryCountAsync(string stream, string group, RedisValue messageId)
{
    var pending = await _db.StreamPendingMessagesAsync(stream, group, 1, RedisValue.Null, messageId, messageId);
    return pending.Length > 0 ? pending[0].DeliveryCount : 0;
}
```

Nota: confira as assinaturas exatas de `StreamAutoClaimAsync` e
`StreamPendingMessagesAsync` no StackExchange.Redis 2.8.0 pelo IntelliSense/compilador;
se `StreamPendingMessagesAsync` exigir ordem diferente de parâmetros
(`count, consumerName, minId, maxId`), ajuste mantendo a semântica.

**Verify**: `dotnet build Atendefy.slnx -c Release` → exit 0.

### Step 2: Isolar falha por entrada e aplicar política de retry/dead-letter no worker

Em `ConversationWorker.cs`:

1. Adicionar constantes junto às existentes (linhas 34–36):

```csharp
private const string DeadLetterStream = "messages.deadletter";
private const int MaxDeliveryAttempts = 3;
private static readonly TimeSpan PendingMinIdle = TimeSpan.FromSeconds(60);
```

2. Substituir o `foreach` do loop (linhas 55–59) por uma chamada a um novo método
que também processa pendências reivindicadas. Formato-alvo do `ExecuteAsync`:

```csharp
while (!stoppingToken.IsCancellationRequested)
{
    try
    {
        // 1º: reprocessa entradas presas (entregues e não-ACKadas há > minIdle)
        var reclaimed = await streams.ClaimPendingAsync(
            StreamName, GroupName, ConsumerName, PendingMinIdle);
        foreach (var entry in reclaimed)
            await ProcessOneAsync(entry, isRedelivery: true);

        var entries = await streams.ReadGroupAsync(StreamName, GroupName, ConsumerName);
        if (entries.Length == 0 && reclaimed.Length == 0)
        {
            await Task.Delay(500, stoppingToken);
            continue;
        }
        foreach (var entry in entries)
            await ProcessOneAsync(entry, isRedelivery: false);
    }
    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
    {
        logger.LogError(ex, "Erro no ConversationWorker");
        await Task.Delay(2000, stoppingToken);
    }
}
```

3. Novo método privado com a política:

```csharp
private async Task ProcessOneAsync(StackExchange.Redis.StreamEntry entry, bool isRedelivery)
{
    try
    {
        await ProcessEntryAsync(entry);
        await streams.AcknowledgeAsync(StreamName, GroupName, entry.Id);
    }
    catch (Exception ex)
    {
        // Falhou: decide entre deixar pendente (retry via autoclaim) ou dead-letter.
        var attempts = isRedelivery
            ? await streams.GetDeliveryCountAsync(StreamName, GroupName, entry.Id)
            : 1;

        if (attempts >= MaxDeliveryAttempts)
        {
            logger.LogError(ex,
                "Entrada {EntryId} descartada para dead-letter após {Attempts} tentativas",
                entry.Id.ToString(), attempts);
            await streams.PublishAsync(DeadLetterStream,
                entry.Values.ToDictionary(v => v.Name.ToString(), v => v.Value.ToString()));
            await streams.AcknowledgeAsync(StreamName, GroupName, entry.Id);
        }
        else
        {
            // NÃO ACKar: fica pendente e será reivindicada pelo autoclaim no próximo ciclo.
            logger.LogWarning(ex,
                "Falha ao processar entrada {EntryId} (tentativa {Attempts}/{Max}) — retry via pending",
                entry.Id.ToString(), attempts, MaxDeliveryAttempts);
        }
    }
}
```

**Verify**: `dotnet build Atendefy.slnx -c Release` → exit 0.

### Step 3: Testes do RedisStreamService

Criar `tests/Atendefy.Tests/Infrastructure/RedisStreamServiceTests.cs`, modelado
em `tests/Atendefy.Tests/Infrastructure/RedisServiceTests.cs` (mock de
`IConnectionMultiplexer` → `IDatabase` via NSubstitute). Casos:

1. `ClaimPendingAsync_DelegatesToStreamAutoClaim` — stub de `StreamAutoClaimAsync`
   retornando um `StreamAutoClaimResult` com 1 entrada; assert que retorna essa entrada
   e que o minIdle foi convertido para ms.
2. `GetDeliveryCountAsync_ReturnsCountFromPendingInfo` — stub de
   `StreamPendingMessagesAsync` com `DeliveryCount = 2`; assert retorno 2.
3. `GetDeliveryCountAsync_WhenNotPending_ReturnsZero` — stub retornando array vazio.

Se `StreamAutoClaimResult`/`StreamPendingMessageInfo` forem structs sem construtor
público utilizável no teste, teste apenas o caso 3 e o caso de delegação via
`Received()` no mock, e registre no PR que os demais são cobertos por smoke manual.

**Verify**: `dotnet test Atendefy.slnx -c Release` → todos passam, incluindo os novos.

## Test plan

- Novos testes: os 3 casos do Step 3 em `RedisStreamServiceTests.cs`.
- Padrão estrutural: `tests/Atendefy.Tests/Infrastructure/RedisServiceTests.cs`.
- Verificação: `dotnet test Atendefy.slnx -c Release` → 0 falhas.
- (Opcional, não bloqueante) Smoke manual com Redis real:
  `docker run -d --rm --name redis-smoke -p 6390:6379 redis:7-alpine`, apontar
  `REDIS_CONNECTION` e simular falha — fora do escopo de CI.

## Done criteria

Machine-checkable. ALL must hold:

- [ ] `dotnet build Atendefy.slnx -c Release` → exit 0, 0 warnings novos
- [ ] `dotnet test Atendefy.slnx -c Release` → exit 0, com os novos testes
- [ ] `grep -n "ClaimPendingAsync" src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs` → 1+ match
- [ ] `grep -n "messages.deadletter" src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs` → 1+ match
- [ ] O `foreach` antigo com ACK incondicional (linhas 55–59 do excerpt) não existe mais
- [ ] `git status` — nenhum arquivo fora do escopo modificado
- [ ] Linha do plano 009 atualizada em `plans/README.md`

## STOP conditions

Stop and report back (do not improvise) if:

- O código em "Current state" não bate com o repositório (drift).
- `StreamAutoClaimAsync` não existir no StackExchange.Redis 2.8.0 instalado
  (não fazer upgrade do pacote — reporte; upgrade de Redis client é decisão à parte).
- Algum teste existente quebrar e a causa não for óbvia em 2 tentativas.
- A mudança parecer exigir tocar `ProcessEntryAsync` além de mover a chamada.

## Maintenance notes

- Se um segundo consumer for adicionado (escalar horizontalmente), `ConsumerName`
  precisa ser único por instância e o autoclaim já cobre crash de qualquer consumer.
- O stream `messages.deadletter` não tem consumidor: monitorar tamanho
  (`XLEN messages.deadletter`) e criar visualização/admin depois (fora deste plano).
- Revisor: conferir que nenhuma exceção em `ProcessOneAsync` escapa para o loop
  (senão o batch volta a abortar) e que o caso `isRedelivery=false` ACKa no sucesso.
