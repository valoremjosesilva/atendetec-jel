# Dashboard Real + Real-time SSE Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exibir métricas reais (conversas, mensagens, tokens, custo) no Dashboard e atualizar o ConversationsPage em tempo real via SSE, eliminando a necessidade de F5.

**Architecture:** O backend expõe `GET /dashboard/stats` que agrega dados do tenant em uma única query. Para real-time, um `ConversationEventEmitter` singleton (System.Threading.Channels) mantém canais por tenant; o `ConversationWorker` emite um evento JSON após cada `PersistAsync`; o endpoint `GET /conversations/stream` (text/event-stream) entrega esses eventos com keepalive a cada 15s para sobreviver a proxies. O frontend usa `EventSource` nativo + `queryClient.invalidateQueries` para atualizar lista e mensagens sem polling.

**Tech Stack:** ASP.NET Core minimal API, System.Threading.Channels, JwtBearer `OnMessageReceived` (token no query param para SSE), browser EventSource API nativo, React Query `invalidateQueries`

---

## Mapa de Arquivos

**Backend — criar:**
- `src/Atendefy.API/Modules/Chatbot/DashboardEndpoints.cs` — GET /dashboard/stats
- `src/Atendefy.API/Modules/Chatbot/IConversationEventEmitter.cs` — interface Subscribe/Unsubscribe/Emit
- `src/Atendefy.API/Modules/Chatbot/ConversationEventEmitter.cs` — singleton, ConcurrentDictionary + lock por tenant

**Backend — modificar:**
- `src/Atendefy.API/Program.cs` — registrar emitter, JwtBearer OnMessageReceived para SSE, MapDashboardEndpoints, atualizar factory do ConversationWorker
- `src/Atendefy.API/Modules/Chatbot/ConversationService.cs` — PersistAsync retorna `Guid` (conversationId)
- `src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs` — adicionar parâmetro IConversationEventEmitter, emitir após PersistAsync
- `src/Atendefy.API/Modules/Chatbot/ConversationEndpoints.cs` — adicionar GET /conversations/stream

**Frontend — criar:**
- `src/Atendefy.Web/src/hooks/useDashboard.ts` — useDashboardStats

**Frontend — modificar:**
- `src/Atendefy.Web/src/types/api.ts` — adicionar DashboardStats
- `src/Atendefy.Web/src/pages/DashboardPage.tsx` — substituir cards estáticos por métricas reais + ações rápidas
- `src/Atendefy.Web/src/pages/ConversationsPage.tsx` — integrar EventSource SSE

---

## Seção A — Dashboard com dados reais

### Task 1: GET /dashboard/stats (backend)

**Files:**
- Create: `src/Atendefy.API/Modules/Chatbot/DashboardEndpoints.cs`
- Modify: `src/Atendefy.API/Program.cs`

Contexto: o tenant tem um schema próprio com tabelas `conversations`, `usage_counters` e `whatsapp_accounts`. O `TenantDbContextFactory.Create(schemaName)` retorna um `TenantDbContext` para esse schema. O padrão `ResolveSchemaAsync` lê o `tenant_id` do JWT e busca o `SchemaName` na `PublicDbContext`. O `UsageCounter` tem `Month` (string `"yyyy-MM"`) como chave primária, `MessagesSent`, `TokensConsumed` e `CostUsd`.

- [ ] **Criar DashboardEndpoints.cs**

```csharp
using Atendefy.API.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Chatbot;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dashboard/stats", async (
            TenantDbContextFactory dbFactory,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (schemaName, error) = await ResolveSchemaAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            await using var db = dbFactory.Create(schemaName);

            var now = DateTime.UtcNow;
            var firstOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var currentMonth = now.ToString("yyyy-MM");

            var totalConversations = await db.Conversations.CountAsync();
            var conversationsThisMonth = await db.Conversations
                .CountAsync(c => c.StartedAt >= firstOfMonth);

            var usage = await db.UsageCounters.FindAsync(currentMonth);

            var waAccount = await db.WhatsAppAccounts
                .Select(w => new { w.Status })
                .FirstOrDefaultAsync();

            return Results.Ok(new
            {
                totalConversations,
                conversationsThisMonth,
                messagesThisMonth  = usage?.MessagesSent ?? 0,
                tokensThisMonth    = usage?.TokensConsumed ?? 0,
                costThisMonth      = usage?.CostUsd ?? 0m,
                whatsAppStatus     = waAccount?.Status ?? "none"
            });
        })
        .WithTags("Dashboard")
        .RequireAuthorization();

        return app;
    }

    private static async Task<(string SchemaName, string? Error)> ResolveSchemaAsync(
        HttpContext ctx, PublicDbContext publicDb)
    {
        var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            return (string.Empty, "Token inválido");
        var tenant = await publicDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return (string.Empty, "Tenant não encontrado");
        return (tenant.SchemaName, null);
    }
}
```

- [ ] **Adicionar `app.MapDashboardEndpoints()` em Program.cs**

Em `src/Atendefy.API/Program.cs`, logo após `app.MapConversationEndpoints();`:

```csharp
app.MapDashboardEndpoints();
```

- [ ] **Rebuild e testar**

```powershell
cd C:\Projetos\JEL\JEL\Atendefy\infra
docker compose build atendefy-api 2>&1 | Select-Object -Last 5
docker compose up -d atendefy-api 2>&1 | Select-Object -Last 5
```

```powershell
$token = (curl.exe -s -X POST "http://localhost:8080/auth/login" `
  -H "Content-Type: application/json" -H "X-Tenant-Key: jel" `
  -d '{"email":"josedudev@gmail.com","password":"Test@123"}' | ConvertFrom-Json).accessToken

curl.exe -s "http://localhost:8080/dashboard/stats" -H "Authorization: Bearer $token"
```

Resposta esperada:
```json
{
  "totalConversations": 2,
  "conversationsThisMonth": 2,
  "messagesThisMonth": 3,
  "tokensThisMonth": 156,
  "costThisMonth": 0.0,
  "whatsAppStatus": "connecting"
}
```

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/Chatbot/DashboardEndpoints.cs src/Atendefy.API/Program.cs
git commit -m "feat: add GET /dashboard/stats endpoint"
```

---

### Task 2: Dashboard frontend — tipo, hook e página

**Files:**
- Modify: `src/Atendefy.Web/src/types/api.ts`
- Create: `src/Atendefy.Web/src/hooks/useDashboard.ts`
- Modify: `src/Atendefy.Web/src/pages/DashboardPage.tsx`

Contexto: o `apiClient` de `@/api/client` usa axios com base URL `/api`. O interceptor injeta o `Authorization: Bearer <token>` automaticamente a partir do `authStore`. `useQuery` com `refetchInterval: 60_000` re-busca os dados a cada minuto.

- [ ] **Adicionar DashboardStats em `src/Atendefy.Web/src/types/api.ts`**

No final do arquivo, adicionar:

```typescript
export interface DashboardStats {
  totalConversations: number;
  conversationsThisMonth: number;
  messagesThisMonth: number;
  tokensThisMonth: number;
  costThisMonth: number;
  whatsAppStatus: string;
}
```

- [ ] **Criar `src/Atendefy.Web/src/hooks/useDashboard.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type { DashboardStats } from '@/types/api';

export function useDashboardStats() {
  return useQuery({
    queryKey: ['dashboard-stats'],
    queryFn: () =>
      apiClient.get<DashboardStats>('/dashboard/stats').then((r) => r.data),
    refetchInterval: 60_000,
  });
}
```

- [ ] **Substituir `src/Atendefy.Web/src/pages/DashboardPage.tsx`**

```typescript
import { Link } from 'react-router-dom';
import { Bot, CreditCard, MessageSquare, Wifi } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { useDashboardStats } from '@/hooks/useDashboard';

export default function DashboardPage() {
  const { data: stats, isLoading } = useDashboardStats();
  const waConnected = stats?.whatsAppStatus === 'open';

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Dashboard</h1>
        <Badge variant={waConnected ? 'default' : 'secondary'}>
          WhatsApp {isLoading ? '…' : (waConnected ? 'conectado' : (stats?.whatsAppStatus ?? 'none'))}
        </Badge>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          title="Conversas este mês"
          value={isLoading ? '…' : String(stats?.conversationsThisMonth ?? 0)}
          sub={`${stats?.totalConversations ?? 0} no total`}
          icon={<MessageSquare className="h-4 w-4 text-muted-foreground" />}
        />
        <StatCard
          title="Mensagens este mês"
          value={isLoading ? '…' : String(stats?.messagesThisMonth ?? 0)}
          icon={<MessageSquare className="h-4 w-4 text-muted-foreground" />}
        />
        <StatCard
          title="Tokens consumidos"
          value={isLoading ? '…' : (stats?.tokensThisMonth ?? 0).toLocaleString('pt-BR')}
          icon={<Bot className="h-4 w-4 text-muted-foreground" />}
        />
        <StatCard
          title="Custo estimado (USD)"
          value={isLoading ? '…' : `$${(stats?.costThisMonth ?? 0).toFixed(4)}`}
          icon={<CreditCard className="h-4 w-4 text-muted-foreground" />}
        />
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <ActionCard title="WhatsApp" description="Contas e webhooks" icon={<Wifi className="h-5 w-5" />} to="/whatsapp" />
        <ActionCard title="IA" description="Provedor e system prompt" icon={<Bot className="h-5 w-5" />} to="/ai-config" />
        <ActionCard title="Conversas" description="Histórico de atendimentos" icon={<MessageSquare className="h-5 w-5" />} to="/conversations" />
        <ActionCard title="Billing" description="Planos e assinaturas" icon={<CreditCard className="h-5 w-5" />} to="/billing" />
      </div>
    </div>
  );
}

function StatCard({
  title,
  value,
  sub,
  icon,
}: {
  title: string;
  value: string;
  sub?: string;
  icon: React.ReactNode;
}) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
        <CardTitle className="text-sm font-medium">{title}</CardTitle>
        {icon}
      </CardHeader>
      <CardContent>
        <div className="text-2xl font-bold">{value}</div>
        {sub && <p className="text-xs text-muted-foreground mt-1">{sub}</p>}
      </CardContent>
    </Card>
  );
}

function ActionCard({
  title,
  description,
  icon,
  to,
}: {
  title: string;
  description: string;
  icon: React.ReactNode;
  to: string;
}) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
        <CardTitle className="text-sm font-medium">{title}</CardTitle>
        {icon}
      </CardHeader>
      <CardContent>
        <p className="text-xs text-muted-foreground mb-3">{description}</p>
        <Button size="sm" variant="outline" render={<Link to={to} />}>
          Acessar
        </Button>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Rebuild frontend e verificar em http://localhost:3000/dashboard**

```powershell
cd C:\Projetos\JEL\JEL\Atendefy\infra
docker compose build atendefy-web 2>&1 | Select-Object -Last 5
docker compose up -d atendefy-web 2>&1 | Select-Object -Last 5
```

Abrir `http://localhost:3000/dashboard` depois de logar com `josedudev@gmail.com` / `Test@123` (subdomain `jel`). Os 4 cards de stats devem mostrar números reais do banco.

- [ ] **Commit**

```powershell
git add src/Atendefy.Web/src/types/api.ts `
      src/Atendefy.Web/src/hooks/useDashboard.ts `
      src/Atendefy.Web/src/pages/DashboardPage.tsx
git commit -m "feat: dashboard with real metrics from tenant stats"
```

---

## Seção B — Real-time SSE no ConversationsPage

### Task 3: IConversationEventEmitter + ConversationEventEmitter

**Files:**
- Create: `src/Atendefy.API/Modules/Chatbot/IConversationEventEmitter.cs`
- Create: `src/Atendefy.API/Modules/Chatbot/ConversationEventEmitter.cs`

Contexto: o emitter é um singleton que mantém uma lista de `ChannelWriter<string>` por `tenantId`. O `ConversationWorker` (também singleton) chama `Emit` após persistir cada mensagem. O endpoint SSE chama `Subscribe` ao conectar e `Unsubscribe` no `finally` ao desconectar. O `lock` garante que Subscribe/Unsubscribe/Emit não corrompem a lista sob concorrência. `Emit` copia a lista antes de iterar para não segurar o lock durante writes.

- [ ] **Criar IConversationEventEmitter.cs**

```csharp
using System.Threading.Channels;

namespace Atendefy.API.Modules.Chatbot;

public interface IConversationEventEmitter
{
    void Subscribe(string tenantId, ChannelWriter<string> writer);
    void Unsubscribe(string tenantId, ChannelWriter<string> writer);
    void Emit(string tenantId, string data);
}
```

- [ ] **Criar ConversationEventEmitter.cs**

```csharp
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Atendefy.API.Modules.Chatbot;

public class ConversationEventEmitter : IConversationEventEmitter
{
    private readonly ConcurrentDictionary<string, List<ChannelWriter<string>>> _subs = new();
    private readonly object _lock = new();

    public void Subscribe(string tenantId, ChannelWriter<string> writer)
    {
        lock (_lock)
        {
            _subs.GetOrAdd(tenantId, _ => new List<ChannelWriter<string>>()).Add(writer);
        }
    }

    public void Unsubscribe(string tenantId, ChannelWriter<string> writer)
    {
        lock (_lock)
        {
            if (_subs.TryGetValue(tenantId, out var list))
                list.Remove(writer);
        }
    }

    public void Emit(string tenantId, string data)
    {
        List<ChannelWriter<string>> snapshot;
        lock (_lock)
        {
            if (!_subs.TryGetValue(tenantId, out var list)) return;
            snapshot = list.ToList();
        }
        foreach (var writer in snapshot)
            writer.TryWrite(data);
    }
}
```

- [ ] **Registrar como singleton em Program.cs**

Em `src/Atendefy.API/Program.cs`, na seção `// Chatbot`, antes do `AddHostedService` do `ConversationWorker`:

```csharp
builder.Services.AddSingleton<IConversationEventEmitter, ConversationEventEmitter>();
```

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/Chatbot/IConversationEventEmitter.cs `
      src/Atendefy.API/Modules/Chatbot/ConversationEventEmitter.cs `
      src/Atendefy.API/Program.cs
git commit -m "feat: add ConversationEventEmitter singleton for SSE fan-out"
```

---

### Task 4: PersistAsync retorna Guid + Worker emite evento

**Files:**
- Modify: `src/Atendefy.API/Modules/Chatbot/ConversationService.cs`
- Modify: `src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs`
- Modify: `src/Atendefy.API/Program.cs`

Contexto: `ConversationService.PersistAsync` atualmente retorna `Task`. Precisa retornar `Task<Guid>` com o `conversation.Id` para o Worker poder emitir o evento com o `conversationId` correto. O `ConversationWorker` é instanciado manualmente em `Program.cs` — precisa receber `IConversationEventEmitter` como novo parâmetro. O evento emitido é JSON: `{"type":"message_added","conversationId":"<guid>"}`.

- [ ] **Alterar assinatura de PersistAsync em ConversationService.cs**

Localizar a linha:
```csharp
public static async Task PersistAsync(
```

Substituir por:
```csharp
public static async Task<Guid> PersistAsync(
```

Localizar o último `await db.SaveChangesAsync();` (segunda chamada, no final do método) e adicionar `return conversation.Id;` logo após:

```csharp
        await db.SaveChangesAsync();
        return conversation.Id;
    }
```

O método completo modificado fica:

```csharp
public static async Task<Guid> PersistAsync(
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
        await db.SaveChangesAsync();
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
    return conversation.Id;
}
```

- [ ] **Adicionar IConversationEventEmitter ao ConversationWorker**

Em `src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs`:

1. Adicionar `using System.Text.Json;` no topo.

2. Adicionar parâmetro `IConversationEventEmitter emitter` ao construtor (após `string encryptionKey`):

```csharp
public class ConversationWorker(
    RedisStreamService streams,
    ConversationService conversationService,
    TenantDbContextFactory tenantDbFactory,
    AIProviderFactory aiFactory,
    WhatsAppProviderFactory whatsAppFactory,
    TenantRateLimiter rateLimiter,
    string encryptionKey,
    IConversationEventEmitter emitter,
    ILogger<ConversationWorker> logger) : BackgroundService
```

3. No método `ProcessEntryAsync`, substituir a chamada ao `PersistAsync` e o `LogInformation` por:

```csharp
        var conversationId = await ConversationService.PersistAsync(
            tenantDbFactory, msg.SchemaName, msg.ContactPhone,
            msg.MessageText, aiResult.Content, aiResult.TokensUsed);

        emitter.Emit(msg.TenantId, JsonSerializer.Serialize(
            new { type = "message_added", conversationId }));

        logger.LogInformation("Mensagem processada para tenant {TenantId}, contato {Phone}",
            msg.TenantId, msg.ContactPhone);
```

- [ ] **Atualizar factory do ConversationWorker em Program.cs**

Localizar a chamada `builder.Services.AddHostedService(sp => new ConversationWorker(...)` e adicionar `sp.GetRequiredService<IConversationEventEmitter>()` antes do `ILogger`:

```csharp
builder.Services.AddHostedService(sp => new ConversationWorker(
    sp.GetRequiredService<RedisStreamService>(),
    sp.GetRequiredService<ConversationService>(),
    sp.GetRequiredService<TenantDbContextFactory>(),
    sp.GetRequiredService<AIProviderFactory>(),
    sp.GetRequiredService<WhatsAppProviderFactory>(),
    sp.GetRequiredService<TenantRateLimiter>(),
    encryptionKey,
    sp.GetRequiredService<IConversationEventEmitter>(),
    sp.GetRequiredService<ILogger<ConversationWorker>>()));
```

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/Chatbot/ConversationService.cs `
      src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs `
      src/Atendefy.API/Program.cs
git commit -m "feat: emit SSE event after conversation persist"
```

---

### Task 5: Endpoint GET /conversations/stream (SSE)

**Files:**
- Modify: `src/Atendefy.API/Modules/Chatbot/ConversationEndpoints.cs`
- Modify: `src/Atendefy.API/Program.cs`

Contexto: o browser `EventSource` não suporta headers customizados, então o JWT vem em `?token=<jwt>`. O middleware JwtBearer lê o token do header `Authorization: Bearer` por padrão. Para o path `/conversations/stream`, configuramos `OnMessageReceived` para ler de `?token=`. O endpoint responde com `Content-Type: text/event-stream`. O header `X-Accel-Buffering: no` desabilita o buffer do nginx. Um keepalive `: ping\n\n` é enviado a cada 15 segundos para evitar que proxies fechem a conexão por timeout. O `CancellationToken` é cancelado automaticamente quando o browser desconecta.

- [ ] **Configurar JwtBearer OnMessageReceived em Program.cs**

Na seção de configuração do JwtBearer (`.AddJwtBearer(opt => { ... })`), adicionar o bloco `opt.Events` logo após `opt.MapInboundClaims = false;`:

```csharp
opt.Events = new JwtBearerEvents
{
    OnMessageReceived = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/conversations/stream"))
        {
            var t = ctx.Request.Query["token"].ToString();
            if (!string.IsNullOrEmpty(t)) ctx.Token = t;
        }
        return Task.CompletedTask;
    }
};
```

Adicionar o `using` necessário no topo do arquivo (se não existir):
```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
```
(já deve estar presente)

- [ ] **Adicionar endpoint SSE em ConversationEndpoints.cs**

Adicionar `using System.Threading.Channels;` no topo do arquivo.

Dentro de `MapConversationEndpoints`, antes do `return app;`, adicionar o novo endpoint:

```csharp
        group.MapGet("/stream", async (
            HttpContext ctx,
            IConversationEventEmitter emitter,
            CancellationToken ct) =>
        {
            ctx.Response.Headers["Content-Type"]      = "text/event-stream";
            ctx.Response.Headers["Cache-Control"]     = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
            if (string.IsNullOrEmpty(tenantIdStr))
            {
                ctx.Response.StatusCode = 401;
                return;
            }

            var channel = Channel.CreateBounded<string>(
                new BoundedChannelOptions(50) { FullMode = BoundedChannelFullMode.DropOldest });

            emitter.Subscribe(tenantIdStr, channel.Writer);

            try
            {
                await ctx.Response.WriteAsync(": connected\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);

                while (!ct.IsCancellationRequested)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(15_000);

                    try
                    {
                        var data = await channel.Reader.ReadAsync(cts.Token);
                        await ctx.Response.WriteAsync($"data: {data}\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await ctx.Response.WriteAsync(": ping\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                emitter.Unsubscribe(tenantIdStr, channel.Writer);
            }
        });
```

Nota: o endpoint herda `.RequireAuthorization()` do grupo; por isso o `tenant_id` em `ctx.User` é garantido pelo middleware (mas validamos defensivamente).

- [ ] **Rebuild e testar o endpoint SSE manualmente**

```powershell
cd C:\Projetos\JEL\JEL\Atendefy\infra
docker compose build atendefy-api 2>&1 | Select-Object -Last 5
docker compose up -d atendefy-api 2>&1 | Select-Object -Last 5
```

Em um terminal, abrir conexão SSE (mantém aberta):
```powershell
$token = (curl.exe -s -X POST "http://localhost:8080/auth/login" `
  -H "Content-Type: application/json" -H "X-Tenant-Key: jel" `
  -d '{"email":"josedudev@gmail.com","password":"Test@123"}' | ConvertFrom-Json).accessToken

curl.exe -N "http://localhost:8080/conversations/stream?token=$token"
```

Esperado: o terminal exibe `: connected` e depois `: ping` a cada 15 segundos. Em outro terminal, enviar um webhook:
```powershell
curl.exe -X POST "http://localhost:8080/webhooks/evolution?token=3ce368504e444f9092d60fbb922c57f9" `
  -H "Content-Type: application/json" `
  -d '{"event":"messages.upsert","data":{"key":{"fromMe":false,"remoteJid":"5511987654321@s.whatsapp.net"},"message":{"conversation":"teste sse"}}}'
```

Esperado: no terminal do SSE aparecer algo como:
```
data: {"type":"message_added","conversationId":"bbb2e2be-..."}
```

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/Chatbot/ConversationEndpoints.cs `
      src/Atendefy.API/Program.cs
git commit -m "feat: add GET /conversations/stream SSE endpoint with keepalive"
```

---

### Task 6: ConversationsPage com EventSource SSE

**Files:**
- Modify: `src/Atendefy.Web/src/pages/ConversationsPage.tsx`

Contexto: `EventSource` é a API nativa do browser para SSE. A URL usa o token JWT como query param (`/api/conversations/stream?token=...`) porque EventSource não suporta headers customizados. O `queryClient` é obtido via `useQueryClient()` do React Query. `useAuthStore.getState().accessToken` lê o token do Zustand sem causar re-render. Um `useRef` guarda o `selectedId` atual para usar dentro do handler sem criar nova dependência no `useEffect`. Se o EventSource falha 5 vezes, é fechado para evitar loops infinitos com token expirado.

- [ ] **Substituir ConversationsPage.tsx**

```typescript
import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { MessageSquare, Phone } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import { useConversations, useConversationMessages } from '@/hooks/useConversations';
import { useAuthStore } from '@/stores/authStore';

function formatTime(dateStr: string): string {
  const d = new Date(dateStr);
  const now = new Date();
  const isToday = d.toDateString() === now.toDateString();
  if (isToday) return d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
  return d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' });
}

export default function ConversationsPage() {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const { data, isLoading, isError } = useConversations();
  const { data: detail, isLoading: loadingMessages, isError: messagesError } =
    useConversationMessages(selectedId);

  const queryClient = useQueryClient();
  const selectedIdRef = useRef(selectedId);
  useEffect(() => { selectedIdRef.current = selectedId; }, [selectedId]);

  useEffect(() => {
    const token = useAuthStore.getState().accessToken;
    if (!token) return;

    const url = `/api/conversations/stream?token=${encodeURIComponent(token)}`;
    const es = new EventSource(url);
    let failures = 0;

    es.onmessage = (e) => {
      try {
        const { conversationId } = JSON.parse(e.data) as { conversationId: string };
        queryClient.invalidateQueries({ queryKey: ['conversations'] });
        queryClient.invalidateQueries({ queryKey: ['conversations', conversationId, 'messages'] });
        queryClient.invalidateQueries({ queryKey: ['dashboard-stats'] });
      } catch {
        // ignore malformed events (keepalive comments are filtered by EventSource)
      }
    };

    es.onerror = () => {
      failures++;
      if (failures > 5) es.close();
    };

    return () => es.close();
  }, [queryClient]);

  return (
    <div className="flex h-[calc(100vh-8rem)] gap-4">
      {/* Painel esquerdo: lista de conversas */}
      <div className="w-80 shrink-0 flex flex-col border rounded-lg overflow-hidden bg-card">
        <div className="p-4 border-b">
          <h1 className="text-lg font-semibold">Conversas</h1>
          {data && (
            <p className="text-xs text-muted-foreground mt-0.5">{data.total} conversa(s)</p>
          )}
        </div>

        <div className="flex-1 overflow-y-auto">
          {isLoading && (
            <p className="p-4 text-sm text-muted-foreground">Carregando...</p>
          )}
          {isError && (
            <p className="p-4 text-sm text-destructive">Erro ao carregar conversas.</p>
          )}
          {!isLoading && !isError && data?.conversations.length === 0 && (
            <div className="p-6 text-center text-sm text-muted-foreground">
              <MessageSquare className="h-8 w-8 mx-auto mb-2 opacity-30" />
              <p>Nenhuma conversa ainda.</p>
              <p className="mt-1 text-xs">Envie uma mensagem via WhatsApp para começar.</p>
            </div>
          )}
          {data?.conversations.map((conv) => (
            <button
              key={conv.id}
              type="button"
              className={cn(
                'w-full text-left px-4 py-3 border-b hover:bg-accent transition-colors',
                selectedId === conv.id && 'bg-accent'
              )}
              onClick={() => setSelectedId(conv.id)}
            >
              <div className="flex items-center justify-between mb-1">
                <span className="text-sm font-medium truncate">{conv.contactPhone}</span>
                <span className="text-xs text-muted-foreground shrink-0 ml-2">
                  {formatTime(conv.lastMessageAt)}
                </span>
              </div>
              <Badge variant="outline" className="text-xs py-0 h-5">
                {conv.messageCount} msgs
              </Badge>
            </button>
          ))}
        </div>
      </div>

      {/* Painel direito: mensagens */}
      <div className="flex-1 flex flex-col border rounded-lg overflow-hidden bg-card">
        {!selectedId ? (
          <div className="flex-1 flex items-center justify-center text-muted-foreground">
            <div className="text-center">
              <MessageSquare className="h-12 w-12 mx-auto mb-3 opacity-20" />
              <p className="text-sm">Selecione uma conversa</p>
            </div>
          </div>
        ) : (
          <>
            <div className="px-4 py-3 border-b flex items-center gap-2 shrink-0">
              <Phone className="h-4 w-4 text-muted-foreground" />
              <span className="font-medium text-sm">{detail?.contactPhone ?? '…'}</span>
              {detail && (
                <span className="text-xs text-muted-foreground ml-auto">
                  desde {new Date(detail.startedAt).toLocaleDateString('pt-BR')}
                </span>
              )}
            </div>

            <div className="flex-1 overflow-y-auto p-4 space-y-3">
              {loadingMessages && (
                <p className="text-sm text-center text-muted-foreground py-4">
                  Carregando mensagens…
                </p>
              )}
              {messagesError && (
                <p className="text-sm text-center text-destructive py-4">
                  Erro ao carregar mensagens.
                </p>
              )}
              {detail?.messages.map((msg) => (
                <div
                  key={msg.id}
                  className={cn('flex', msg.role === 'user' ? 'justify-start' : 'justify-end')}
                >
                  <div
                    className={cn(
                      'max-w-[75%] rounded-2xl px-4 py-2 text-sm',
                      msg.role === 'user'
                        ? 'bg-muted rounded-tl-sm'
                        : 'bg-primary text-primary-foreground rounded-tr-sm'
                    )}
                  >
                    <p className="whitespace-pre-wrap break-words">{msg.content}</p>
                    <p
                      className={cn(
                        'text-xs mt-1',
                        msg.role === 'user'
                          ? 'text-muted-foreground'
                          : 'text-primary-foreground/70'
                      )}
                    >
                      {formatTime(msg.createdAt)}
                    </p>
                  </div>
                </div>
              ))}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Rebuild frontend**

```powershell
cd C:\Projetos\JEL\JEL\Atendefy\infra
docker compose build atendefy-web 2>&1 | Select-Object -Last 5
docker compose up -d atendefy-web 2>&1 | Select-Object -Last 5
```

- [ ] **Testar real-time end-to-end**

1. Abrir `http://localhost:3000/conversations` no browser após login
2. Abrir as DevTools → Network → filtrar por `stream` → confirmar que a conexão SSE está ativa (status `pending`)
3. Em um terminal, enviar uma mensagem:

```powershell
curl.exe -X POST "http://localhost:8080/webhooks/evolution?token=3ce368504e444f9092d60fbb922c57f9" `
  -H "Content-Type: application/json" `
  -d '{"event":"messages.upsert","data":{"key":{"fromMe":false,"remoteJid":"5511987654321@s.whatsapp.net"},"message":{"conversation":"mensagem em tempo real!"}}}'
```

Esperado: sem F5, a lista de conversas atualiza em ~1-3 segundos. Se a conversa `5511987654321` estiver selecionada, as mensagens novas aparecem imediatamente.

- [ ] **Commit**

```powershell
git add src/Atendefy.Web/src/pages/ConversationsPage.tsx
git commit -m "feat: real-time ConversationsPage via SSE EventSource"
```

---

## Referência Rápida

**Credenciais dev:**
- Login: `josedudev@gmail.com` / `Test@123` (subdomain `jel`, header `X-Tenant-Key: jel`)
- Webhook token: `3ce368504e444f9092d60fbb922c57f9`
- API: `http://localhost:8080` | Frontend: `http://localhost:3000`

**Rebuild e restart (API):**
```powershell
cd C:\Projetos\JEL\JEL\Atendefy\infra
docker compose build atendefy-api && docker compose up -d atendefy-api
```

**Rebuild e restart (Frontend):**
```powershell
cd C:\Projetos\JEL\JEL\Atendefy\infra
docker compose build atendefy-web && docker compose up -d atendefy-web
```

**Ver logs da API:**
```powershell
docker logs infra-atendefy-api-1 --since 30s -f
```
