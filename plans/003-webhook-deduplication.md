# Plano 003: Deduplicar mensagens de webhook (Meta e Evolution)

> **Instruções para o executor**: Siga este plano passo a passo. Execute cada verificação antes
> de avançar. Em condições de PARE, pare e reporte — não improvise.
>
> **Verificação de drift (execute primeiro)**:
> `git diff --stat e805859..HEAD -- src/Atendefy.API/Modules/Webhooks/WebhookEndpoints.cs src/Atendefy.API/Modules/Webhooks/Models/MetaWebhookPayload.cs src/Atendefy.API/Modules/Webhooks/Models/EvolutionWebhookPayload.cs`
> Em caso de divergência com os trechos abaixo, trate como PARE.

## Status

- **Prioridade**: P1
- **Esforço**: M (Médio — ~1 dia)
- **Risco**: MÉDIO
- **Depende de**: nenhum
- **Categoria**: correção de bug
- **Escrito no commit**: `e805859`, 2026-06-28

## Por que isso importa

Tanto o Meta Cloud API quanto a Evolution API repetem webhooks em caso de falha de rede, timeout,
ou reboot do servidor Evolution. Atualmente, `WebhookEndpoints.cs` não tem nenhuma verificação
de duplicidade: cada mensagem recebida é publicada no Redis Stream incondicionalmente. O
`ConversationWorker` processa ambas, criando:
- **Conversas duplicadas** se o mesmo telefone enviar a mesma mensagem e a conversa for criada
  duas vezes no intervalo de processamento;
- **Respostas em duplicata** — o usuário final recebe a mesma resposta da IA duas vezes.

A solução é extrair o ID único de cada mensagem (disponível no payload de ambos os provedores,
mas não mapeado nos models atuais) e usar o Redis para deduplicar com TTL de 24h.

## Estado atual

**`MetaMessage`** — `src/Atendefy.API/Modules/Webhooks/Models/MetaWebhookPayload.cs:29-34`:**
```csharp
public record MetaMessage(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] MetaMessageText? Text,
    [property: JsonPropertyName("interactive")] MetaInteractive? Interactive = null
);
```
> ⚠️ O campo `"id"` da mensagem Meta NÃO está mapeado. Precisa ser adicionado.

**`EvolutionKey`** — `src/Atendefy.API/Modules/Webhooks/Models/EvolutionWebhookPayload.cs:17-20`:
```csharp
public record EvolutionKey(
    [property: JsonPropertyName("remoteJid")] string RemoteJid,
    [property: JsonPropertyName("fromMe")] bool FromMe
);
```
> ⚠️ O campo `"id"` da mensagem Evolution NÃO está mapeado. Precisa ser adicionado.

**Loop Meta no handler** — `WebhookEndpoints.cs:53-82`:
```csharp
foreach (var msg in change.Value.Messages ?? [])
{
    var messageText = msg.Type switch { ... };
    if (string.IsNullOrWhiteSpace(messageText)) continue;
    // ... sem verificação de duplicidade ...
    await PublishAsync(streams, new InboundMessage(...));
}
```

**Loop Evolution no handler** — `WebhookEndpoints.cs:100-121`:
```csharp
var payload = await ctx.Request.ReadFromJsonAsync<EvolutionWebhookPayload>();
if (payload is null || payload.Event != "messages.upsert") return Results.Ok();
if (payload.Data.Key.FromMe) return Results.Ok();
// ... sem verificação de duplicidade ...
await PublishAsync(streams, new InboundMessage(...));
```

**`RedisService`** disponível — `src/Atendefy.API/Infrastructure/Cache/RedisService.cs`:
```csharp
Task<bool> ExistsAsync(string key)
Task SetAsync(string key, string value, TimeSpan? expiry = null)
```

## Comandos necessários

| Propósito    | Comando                                                          | Esperado     |
|--------------|------------------------------------------------------------------|--------------|
| Build        | `dotnet build Atendefy.slnx -c Release --no-restore`             | exit 0       |
| Todos testes | `dotnet test Atendefy.slnx -c Release`                          | todos passam |

## Escopo

**Em escopo**:
- `src/Atendefy.API/Modules/Webhooks/Models/MetaWebhookPayload.cs`
- `src/Atendefy.API/Modules/Webhooks/Models/EvolutionWebhookPayload.cs`
- `src/Atendefy.API/Modules/Webhooks/WebhookEndpoints.cs`

**Fora do escopo** (não tocar):
- `InboundMessage.cs` — record de transporte não precisa carregar o ID externo
- `ConversationWorker.cs` — a deduplicação acontece antes da publicação no stream
- `RedisStreamService.cs` — não precisa de novos métodos
- Migrations de banco — nenhuma mudança de schema

## Git workflow

- Branch: `advisor/003-webhook-deduplication`
- Commits: um commit para os models, um para o handler
- Mensagem: `fix(webhooks): deduplicar mensagens Meta e Evolution via Redis`

## Passos

### Passo 1: Adicionar campo `Id` ao `MetaMessage`

Em `MetaWebhookPayload.cs`, atualize o record `MetaMessage` para incluir o campo `id`:

```csharp
public record MetaMessage(
    [property: JsonPropertyName("id")] string Id,          // <-- adicionar
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] MetaMessageText? Text,
    [property: JsonPropertyName("interactive")] MetaInteractive? Interactive = null
);
```

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

### Passo 2: Adicionar campo `Id` ao `EvolutionKey`

Em `EvolutionWebhookPayload.cs`, atualize o record `EvolutionKey`:

```csharp
public record EvolutionKey(
    [property: JsonPropertyName("id")] string? Id,          // <-- adicionar (nullable: nem todo evento tem)
    [property: JsonPropertyName("remoteJid")] string RemoteJid,
    [property: JsonPropertyName("fromMe")] bool FromMe
);
```

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

### Passo 3: Injetar `RedisService` em `WebhookEndpoints` e adicionar helper de dedup

No início de `WebhookEndpoints.cs`, localize o método `MapWebhookEndpoints`. Adicione um método
privado estático auxiliar ao final da classe (antes do `}`):

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

Para injetar `RedisService` nos endpoints: ambos os handlers (Meta POST e Evolution POST) precisam
receber `RedisService redis` como parâmetro no delegate do endpoint. Localize as assinaturas:

**Meta POST** — mude de:
```csharp
group.MapPost("/meta", async (
    HttpContext ctx,
    PublicDbContext publicDb,
    RedisStreamService streams,
    MetaWebhookValidator validator) =>
```
Para:
```csharp
group.MapPost("/meta", async (
    HttpContext ctx,
    PublicDbContext publicDb,
    RedisStreamService streams,
    MetaWebhookValidator validator,
    RedisService redis) =>           // <-- adicionar
```

**Evolution POST** — mesma mudança:
```csharp
group.MapPost("/evolution", async (
    HttpContext ctx,
    RedisStreamService streams,
    EvolutionWebhookValidator evolutionValidator,
    [FromQuery] string? token,
    RedisService redis) =>           // <-- adicionar
```

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

### Passo 4: Aplicar deduplicação no handler Meta

Dentro do loop `foreach (var msg in change.Value.Messages ?? [])`, logo após a linha que verifica
`if (string.IsNullOrWhiteSpace(messageText)) continue;`, adicione:

```csharp
// Deduplicar: Meta replica webhooks em retry. Pular se já processado.
if (!string.IsNullOrEmpty(msg.Id)
    && await IsDuplicateAsync(redis, "meta", msg.Id))
    continue;
```

O bloco completo do loop ficará:
```csharp
foreach (var msg in change.Value.Messages ?? [])
{
    var messageText = msg.Type switch { ... };
    if (string.IsNullOrWhiteSpace(messageText)) continue;

    if (!string.IsNullOrEmpty(msg.Id)
        && await IsDuplicateAsync(redis, "meta", msg.Id))
        continue;

    var route = await publicDb.WebhookRoutes.FirstOrDefaultAsync(...);
    // ... resto do bloco
    await PublishAsync(streams, new InboundMessage(...));
}
```

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

### Passo 5: Aplicar deduplicação no handler Evolution

Logo após a linha `if (string.IsNullOrWhiteSpace(messageText)) return Results.Ok();`, adicione:

```csharp
// Deduplicar: Evolution pode reenviar o mesmo webhook após restart.
if (!string.IsNullOrEmpty(payload.Data.Key.Id)
    && await IsDuplicateAsync(redis, "evolution", payload.Data.Key.Id))
    return Results.Ok();
```

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

### Passo 6: Executar todos os testes

**Verificar**: `dotnet test Atendefy.slnx -c Release` → todos passam

> Os testes de integração usam Redis mockado (NSubstitute). `ExistsAsync` retornará `false` por
> padrão (mock retorna `RedisValue()` vazio). Isso simula "não duplicado" — comportamento correto.

## Critérios de conclusão

- [ ] `dotnet build Atendefy.slnx -c Release --no-restore` exit 0
- [ ] `dotnet test Atendefy.slnx -c Release` exit 0 (sem regressões)
- [ ] `MetaMessage.Id` existe como propriedade com `JsonPropertyName("id")`
- [ ] `EvolutionKey.Id` existe como propriedade com `JsonPropertyName("id")` (nullable)
- [ ] Handler Meta chama `IsDuplicateAsync` antes de publicar no stream
- [ ] Handler Evolution chama `IsDuplicateAsync` antes de publicar no stream
- [ ] Apenas os 3 arquivos em escopo foram modificados (`git status`)
- [ ] Status atualizado em `plans/README.md`

## Condições de PARE

- O campo `id` de `MetaMessage` ou `EvolutionKey` já existe com nome diferente — adapte sem
  duplicar campos
- `RedisService` não está disponível para injeção nos endpoints (verificar registro em `Program.cs`)
- Os payloads reais de produção não incluem `id` na mensagem Evolution — nesse caso, o check
  `!string.IsNullOrEmpty(payload.Data.Key.Id)` garante que a dedup é simplesmente ignorada
  (safe fallback), mas reporte para investigação

## Notas de manutenção

- TTL de 24h é deliberado: webhooks de retry chegam em segundos a horas, nunca em dias.
- Se a Evolution API mudar o campo `id` em futuros releases, a dedup silenciosamente para de
  funcionar (não quebra). Monitore os logs do ConversationWorker para padrões de mensagem
  duplicada.
- Para Cal.com e Horafy (outros webhooks no mesmo arquivo), deduplicação semelhante pode ser
  adicionada no futuro seguindo o mesmo padrão `IsDuplicateAsync`.
