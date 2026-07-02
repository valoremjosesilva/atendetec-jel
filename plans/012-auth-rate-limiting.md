# Plan 012: Rate-limit em /auth/login, /auth/refresh e /tenants/verify-email

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `plans/README.md`.
>
> **Drift check (run first)**: `git diff --stat f809720..HEAD -- src/Atendefy.API/Modules/Auth/AuthEndpoints.cs src/Atendefy.API/Modules/Tenants/TenantEndpoints.cs`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: S
- **Risk**: LOW
- **Depends on**: none
- **Category**: security
- **Planned at**: commit `f809720`, 2026-07-02

## Why this matters

`/auth/login` aceita tentativas ilimitadas — brute-force de senha é viável (o
BCrypt encarece cada tentativa, mas nada limita a taxa). `/auth/refresh` aceita
tokens forjados sem limite, e `/tenants/verify-email` permite probing de tokens.
Os endpoints vizinhos `/tenants/register` e `/tenants/resend-verification` JÁ
têm rate-limit por IP com a mesma infraestrutura (`TenantRateLimiter`) — este
plano replica o padrão existente nos três endpoints desprotegidos.

## Current state

Arquivos relevantes:

- `src/Atendefy.API/Modules/Auth/AuthEndpoints.cs` — `/auth/login` (linhas 12–30), `/auth/refresh` (32–52), `/auth/logout` (54–58). Nenhum tem rate-limit.
- `src/Atendefy.API/Modules/Tenants/TenantEndpoints.cs` — o PADRÃO a copiar (register, linhas 29–34) e o endpoint `/verify-email` a proteger (linhas 52–69).
- `src/Atendefy.API/Infrastructure/RateLimiting/TenantRateLimiter.cs` — o serviço (registrado como singleton no DI).

O padrão existente a replicar (`TenantEndpoints.cs:29-34`):

```csharp
var ip = ClientIp(ctx);

// 1) Rate-limit por IP (anti-flood de cadastros).
if (!await rateLimiter.IsAllowedAsync(ip, "register", RegisterPerMinutePerIp))
    return Results.Json(new { error = "Muitas tentativas. Aguarde um minuto e tente novamente." },
        statusCode: StatusCodes.Status429TooManyRequests);
```

Helpers existentes em `TenantEndpoints.cs`:
- linha 12: `private const int RegisterPerMinutePerIp = 5;`
- linhas 151–152: `private static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";`
  (o `Program.cs` já configura `UseForwardedHeaders`, então `RemoteIpAddress` é o IP real atrás do Caddy)

API do rate limiter (`TenantRateLimiter.cs:12-21`) — janela de 1 minuto:

```csharp
public async Task<bool> IsAllowedAsync(string key, string scope, int customLimit)
```

Início do `/auth/login` hoje (`AuthEndpoints.cs:12-21`):

```csharp
group.MapPost("/login", async (
    [FromBody] LoginRequest request,
    AuthService authService,
    IHostEnvironment env,
    HttpContext ctx) =>
{
    // TenantId é o subdomínio resolvido pelo TenantResolver middleware
    var tenantIdentifier = ctx.Items["TenantId"]?.ToString();
    if (string.IsNullOrEmpty(tenantIdentifier))
        return Results.Json(new { error = "Tenant não identificado" }, statusCode: 401);
```

Contexto de teste importante: os testes de integração (`ApiFactory`) mockam o
Redis — `StringIncrementAsync` sempre retorna 1 e `StringGetAsync` retorna
RedisValue nula → `GetCounterAsync` = 0 → **o rate limiter sempre PERMITE nos
testes de integração**. Os testes existentes de login não vão quebrar, e o
caminho 429 deve ser testado por teste UNITÁRIO do `TenantRateLimiter`.

## Commands you will need

| Purpose | Command | Expected on success |
|---------|---------|---------------------|
| Build   | `dotnet build Atendefy.slnx -c Release` | exit 0 |
| Testes  | `dotnet test Atendefy.slnx -c Release`  | todos passam |

## Scope

**In scope**:
- `src/Atendefy.API/Modules/Auth/AuthEndpoints.cs`
- `src/Atendefy.API/Modules/Tenants/TenantEndpoints.cs` (somente o endpoint `/verify-email`)
- `tests/Atendefy.Tests/Infrastructure/TenantRateLimiterTests.cs` (criar, se não existir)

**Out of scope**:
- `TenantRateLimiter.cs` — não alterar a implementação.
- `/auth/logout` — só expira cookies; sem valor para atacante.
- Lockout por conta/e-mail (política diferente, decisão de produto — não improvisar).
- `ApiRateLimitFilter` usado em ConversationEndpoints — é filtro para endpoints
  AUTENTICADOS (limite por tenant); aqui o limite é por IP anônimo, padrão do register.

## Git workflow

- Branch: `advisor/012-auth-rate-limiting`
- Conventional commit em português (ex.: `sec(auth): rate-limit por IP em login, refresh e verify-email`)
- Não fazer push nem abrir PR sem instrução do operador.

## Steps

### Step 1: Rate-limit no /auth/login e /auth/refresh

Em `AuthEndpoints.cs`:

1. Adicionar no topo da classe:

```csharp
// Limites por IP (janela de 1 min do TenantRateLimiter). Login mais estrito;
// refresh mais folgado (SPA renova a cada 15 min, mas várias abas concorrem).
private const int LoginPerMinutePerIp = 10;
private const int RefreshPerMinutePerIp = 30;

private static string ClientIp(HttpContext ctx) =>
    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
```

2. No `/auth/login`, adicionar `TenantRateLimiter rateLimiter` aos parâmetros do
   handler e, como PRIMEIRA verificação do corpo (antes do tenantIdentifier):

```csharp
if (!await rateLimiter.IsAllowedAsync(ClientIp(ctx), "login", LoginPerMinutePerIp))
    return Results.Json(new { error = "Muitas tentativas. Aguarde um minuto e tente novamente." },
        statusCode: StatusCodes.Status429TooManyRequests);
```

3. No `/auth/refresh`, idem com scope `"refresh"` e `RefreshPerMinutePerIp`.

Import necessário: `using Atendefy.API.Infrastructure.RateLimiting;`.

**Verify**: `dotnet build Atendefy.slnx -c Release` → exit 0.

### Step 2: Rate-limit no /tenants/verify-email

Em `TenantEndpoints.cs`, no endpoint `/verify-email` (linhas 52–69): adicionar
`TenantRateLimiter rateLimiter` e `HttpContext ctx` aos parâmetros do handler e,
como primeira verificação (antes do check de token vazio):

```csharp
if (!await rateLimiter.IsAllowedAsync(ClientIp(ctx), "verifyemail", RegisterPerMinutePerIp * 2))
    return Results.Json(new { error = "Muitas tentativas. Aguarde um minuto." },
        statusCode: StatusCodes.Status429TooManyRequests);
```

(Limite 10/min: o dobro do register — clicar 2–3x no link do e-mail é legítimo.)

**Verify**: `dotnet build Atendefy.slnx -c Release` → exit 0.

### Step 3: Testes unitários do rate limiter (caminho 429)

Verificar se existe `tests/Atendefy.Tests/Infrastructure/TenantRateLimiterTests.cs`;
se não, criar seguindo o padrão de mock de `tests/Atendefy.Tests/Infrastructure/RedisServiceTests.cs`
(NSubstitute em `IConnectionMultiplexer`/`IDatabase`, `RedisService` real por cima). Casos:

1. `IsAllowedAsync_UnderLimit_ReturnsTrue` — `StringGetAsync` retorna `(RedisValue)3`,
   limite 5 → `true`.
2. `IsAllowedAsync_OverLimit_ReturnsFalse` — `StringGetAsync` retorna `(RedisValue)6`,
   limite 5 → `false`.
3. `IsAllowedAsync_UsesScopedKey` — assert via `Received()` que a chave incrementada
   começa com `ratelimit:login:` quando scope = "login".

**Verify**: `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~TenantRateLimiter"` → 3 passam.

### Step 4: Suíte completa e teste de integração de sanidade

Rodar a suíte completa — os testes de login existentes
(`tests/Atendefy.Tests/Integration/AuthIntegrationTests.cs` e `CookieAuthTests.cs`)
devem continuar passando (Redis mockado sempre permite).

**Verify**: `dotnet test Atendefy.slnx -c Release` → todos passam.

## Test plan

- 3 testes unitários novos (Step 3).
- Suíte completa verde (Step 4) — cobre que o happy path de login/refresh/verify
  não regrediu com os novos parâmetros.

## Done criteria

- [ ] `dotnet build Atendefy.slnx -c Release` → exit 0
- [ ] `dotnet test Atendefy.slnx -c Release` → exit 0, incluindo os novos testes
- [ ] `grep -n "IsAllowedAsync" src/Atendefy.API/Modules/Auth/AuthEndpoints.cs` → 2 matches (login e refresh)
- [ ] `grep -n "verifyemail" src/Atendefy.API/Modules/Tenants/TenantEndpoints.cs` → 1 match
- [ ] `git status` — só arquivos in-scope modificados/criados
- [ ] Linha do plano 012 atualizada em `plans/README.md`

## STOP conditions

- Excerpts não batem com o código (drift) — em especial se `/auth/login` ganhou
  outra assinatura desde `f809720`.
- Algum teste de integração de auth falhar com 429 (indicaria que o mock de Redis
  mudou e o limiter passou a bloquear em Testing — investigar, não afrouxar limite).
- A mudança parecer exigir alterar `TenantRateLimiter` ou `ApiRateLimitFilter`.

## Maintenance notes

- Limites escolhidos (10 login, 30 refresh, 10 verify por IP/min) são pontos de
  partida — operar atrás de NAT corporativo pode exigir ajuste; monitore 429s nos logs.
- Se lockout por conta for desejado depois (5 senhas erradas → bloquear e-mail X
  por 15 min), é feature separada: exige chave por e-mail e mensagem específica.
- Revisor: conferir que o rate-limit vem ANTES de qualquer trabalho (lookup de
  tenant, bcrypt) — o objetivo é barrar barato.
