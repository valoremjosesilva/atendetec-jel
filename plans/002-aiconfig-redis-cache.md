# Plano 002: Cachear AiConfig no Redis para eliminar queries por mensagem

> **Instruções para o executor**: Siga este plano passo a passo. Execute cada verificação antes
> de avançar. Em condições de PARE, pare e reporte — não improvise.
>
> **Verificação de drift (execute primeiro)**:
> `git diff --stat e805859..HEAD -- src/Atendefy.API/Modules/AI/AiConfigService.cs src/Atendefy.API/Infrastructure/Cache/RedisService.cs`
> Se algum arquivo em escopo mudou, compare os trechos de "Estado atual" com o código ao vivo
> antes de continuar.

## Status

- **Prioridade**: P1
- **Esforço**: P (Pequeno — horas)
- **Risco**: BAIXO
- **Depende de**: nenhum (pode rodar em paralelo com 001)
- **Categoria**: performance
- **Escrito no commit**: `e805859`, 2026-06-28

## Por que isso importa

`ConversationWorker` chama `AiConfigService.GetAsync(schemaName)` para cada mensagem recebida.
O método abre uma conexão com o banco e faz `SELECT * FROM ai_configs LIMIT 1` toda vez. A
configuração de IA de um tenant muda raramente (talvez uma vez por mês), mas gera ~100 queries
por minuto por tenant ativo. O Redis já está disponível e é usado para rate-limiting e streams.
Adicionar cache com TTL de 1 hora reduz esse custo a zero na quase totalidade dos casos.

## Estado atual

**`src/Atendefy.API/Modules/AI/AiConfigService.cs`** (arquivo completo, 55 linhas):
```csharp
public class AiConfigService(TenantDbContextFactory dbFactory, string encryptionKey)
{
    // ...

    public async Task<AiConfig?> GetAsync(string schemaName)
    {
        await using var db = dbFactory.Create(schemaName);
        return await db.AiConfigs.FirstOrDefaultAsync();    // <-- query a cada mensagem
    }

    public async Task<Result<AiConfig>> UpsertAsync(string schemaName, AiConfigRequest request)
    {
        // ... valida, faz SaveChangesAsync, retorna Result<AiConfig>.Ok(config/existing)
        // Não invalida cache (cache não existe ainda)
    }
}
```

**`src/Atendefy.API/Infrastructure/Cache/RedisService.cs`** — métodos disponíveis:
```csharp
Task SetAsync(string key, string value, TimeSpan? expiry = null)
Task<string?> GetAsync(string key)
Task DeleteAsync(string key)
Task<bool> ExistsAsync(string key)
```

**Modelo `AiConfig`** (`src/Atendefy.API/Modules/AI/Models/AiConfig.cs`):
```csharp
public class AiConfig
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ApiKeyEncrypted { get; set; }
    public string? Model { get; set; }
    public string? SystemPrompt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
```

**Convenção de cache do projeto:** chave em formato `"entidade:identificador"`, ex.:
`"ratelimit:{schemaName}:{window}"` em `TenantRateLimiter.cs`. Seguir o mesmo padrão.

## Comandos necessários

| Propósito   | Comando                                                   | Esperado     |
|-------------|-----------------------------------------------------------|--------------|
| Build       | `dotnet build Atendefy.slnx -c Release --no-restore`      | exit 0       |
| Testes AI   | `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~AI"` | todos passam |
| Todos testes | `dotnet test Atendefy.slnx -c Release`                   | todos passam |

## Escopo

**Em escopo**:
- `src/Atendefy.API/Modules/AI/AiConfigService.cs`

**Fora do escopo** (não tocar):
- `RedisService.cs` — API pública suficiente, não precisa de novos métodos
- `ConversationWorker.cs` — não muda; continua chamando `GetAsync` normalmente
- `AiConfig.cs` — model não muda
- Qualquer migration de banco — mudança é só na camada de serviço

## Git workflow

- Branch: `advisor/002-aiconfig-redis-cache`
- Mensagem de commit: `perf(ai): cachear AiConfig no Redis com TTL de 1h`
- Não fazer push nem abrir PR, a menos que instruído.

## Passos

### Passo 1: Injetar `RedisService` em `AiConfigService`

`AiConfigService` usa injeção via primary constructor. Adicione `RedisService redis` como
parâmetro:

```csharp
public class AiConfigService(
    TenantDbContextFactory dbFactory,
    string encryptionKey,
    RedisService redis)          // <-- adicionar
```

Também adicione a constante de TTL na classe:

```csharp
private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
```

E o método auxiliar de geração de chave (private):

```csharp
private static string CacheKey(string schemaName) => $"aiconfig:{schemaName}";
```

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → deve falhar com erro de
DI (AiConfigService registrado no DI sem o novo parâmetro). Isso é esperado — corrija no Passo 2.

### Passo 2: Atualizar o registro de DI em `Program.cs`

Localize onde `AiConfigService` é registrado no `Program.cs` (busque por
`AiConfigService` no arquivo). Adicione `RedisService` como argumento:

Antes (padrão aproximado):
```csharp
services.AddSingleton(sp => new AiConfigService(
    sp.GetRequiredService<TenantDbContextFactory>(),
    encryptionKey));
```

Depois:
```csharp
services.AddSingleton(sp => new AiConfigService(
    sp.GetRequiredService<TenantDbContextFactory>(),
    encryptionKey,
    sp.GetRequiredService<RedisService>()));
```

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

### Passo 3: Implementar cache em `GetAsync`

Substitua o corpo de `GetAsync`:

```csharp
public async Task<AiConfig?> GetAsync(string schemaName)
{
    var cached = await redis.GetAsync(CacheKey(schemaName));
    if (cached is not null)
        return System.Text.Json.JsonSerializer.Deserialize<AiConfig>(cached);

    await using var db = dbFactory.Create(schemaName);
    var config = await db.AiConfigs.FirstOrDefaultAsync();
    if (config is not null)
        await redis.SetAsync(CacheKey(schemaName),
            System.Text.Json.JsonSerializer.Serialize(config), CacheTtl);
    return config;
}
```

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

### Passo 4: Invalidar cache em `UpsertAsync`

No final do método `UpsertAsync`, antes do `return`, adicione a invalidação:

```csharp
// Após SaveChangesAsync() e antes do return:
await redis.DeleteAsync(CacheKey(schemaName));
return Result<AiConfig>.Ok(config);   // ou existing, conforme o branch
```

Certifique-se que `DeleteAsync` é chamado em **ambos** os branches (update de existente e
criação de novo).

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

### Passo 5: Executar toda a suite de testes

**Verificar**: `dotnet test Atendefy.slnx -c Release` → todos passam

> Nota: os testes de integração mockam o Redis via `NSubstitute` (veja `ApiFactory.cs:76-83`).
> O mock retorna `RedisValue()` (vazio) para `StringGetAsync`, simulando cache miss.
> Isso faz o `GetAsync` ir ao banco, que é o comportamento correto nos testes.
> Não é necessário criar novos testes para este plano — o comportamento já é exercitado
> pelos testes de integração existentes via o caminho de miss de cache.

## Critérios de conclusão

- [ ] `dotnet build Atendefy.slnx -c Release --no-restore` exit 0
- [ ] `dotnet test Atendefy.slnx -c Release` exit 0 (nenhum teste novo necessário; não regredir existentes)
- [ ] `GetAsync` não acessa o banco quando o cache está populado
- [ ] `UpsertAsync` chama `redis.DeleteAsync(CacheKey(schemaName))` em todos os branches de retorno
- [ ] Apenas `AiConfigService.cs` e `Program.cs` foram modificados (`git status`)
- [ ] Status atualizado em `plans/README.md`

## Condições de PARE

- O código em "Estado atual" não corresponde ao arquivo atual (drift)
- `AiConfigService` é registrado de forma diferente do esperado em `Program.cs` — reporte a
  forma atual antes de continuar
- `RedisService` não está disponível no container DI onde `AiConfigService` é registrado

## Notas de manutenção

- TTL de 1h é conservador. Se tenants reclamarem de demora após mudar config de IA, reduzir para
  15 min ou invalidar proativamente (já feito pelo UpsertAsync).
- O campo `ApiKeyEncrypted` está sendo cacheado no Redis. O valor está criptografado com AES
  (chave de criptografia do servidor), então o risco de exposição via Redis é o mesmo que no BD.
  Não é uma nova superfície de exposição.
- Se houver múltiplas instâncias da API (scale-out horizontal), a invalidação via `DeleteAsync`
  funciona corretamente pois o Redis é compartilhado.
