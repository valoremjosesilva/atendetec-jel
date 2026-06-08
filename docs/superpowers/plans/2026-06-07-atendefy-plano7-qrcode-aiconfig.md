# QR Code WhatsApp + AI Config Editável Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que o tenant configure a IA (sem precisar reinserir a API key) e conecte o WhatsApp escaneando um QR code direto no frontend.

**Architecture:** A. No backend, `AiConfigRequest.ApiKey` torna-se nullable — se existir config e a key vier vazia, mantém a chave encriptada existente. B. Dois novos endpoints WhatsApp (`POST /{id}/connect` e `GET /{id}/status`) chamam a Evolution API via `IHttpClientFactory`; o frontend exibe o QR code e faz polling a cada 3s até `status === "open"`.

**Tech Stack:** ASP.NET Core minimal API, IHttpClientFactory, System.Text.Json, React Query useMutation + useQuery com refetchInterval dinâmico, browser `<img src="data:image/png;base64,...">`.

---

## Mapa de Arquivos

**Backend — modificar:**
- `src/Atendefy.API/Modules/AI/Models/AiConfigRequest.cs` — `string ApiKey` → `string? ApiKey`
- `src/Atendefy.API/Modules/AI/AiConfigService.cs` — skip re-encrypt when ApiKey is null; require ApiKey only for new config
- `src/Atendefy.API/Modules/WhatsApp/WhatsAppAccountService.cs` — add `IHttpClientFactory`, `ConnectAsync`, `GetStatusAsync`
- `src/Atendefy.API/Modules/WhatsApp/WhatsAppEndpoints.cs` — add `POST /{id}/connect`, `GET /{id}/status`

**Frontend — modificar:**
- `src/Atendefy.Web/src/types/api.ts` — add `WhatsAppConnectResponse`, `WhatsAppStatusResponse`
- `src/Atendefy.Web/src/hooks/useWhatsApp.ts` — add `useConnectWhatsApp`, `useWhatsAppStatus`
- `src/Atendefy.Web/src/pages/WhatsAppPage.tsx` — add QR code dialog + polling

---

### Task 1: AI Config — ApiKey opcional no backend

**Files:**
- Modify: `src/Atendefy.API/Modules/AI/Models/AiConfigRequest.cs`
- Modify: `src/Atendefy.API/Modules/AI/AiConfigService.cs`

- [ ] **Alterar `AiConfigRequest` para ApiKey nullable**

Substituir o conteúdo de `src/Atendefy.API/Modules/AI/Models/AiConfigRequest.cs`:

```csharp
namespace Atendefy.API.Modules.AI.Models;

public record AiConfigRequest(
    string Provider,
    string? ApiKey,
    string Model,
    string SystemPrompt
);
```

- [ ] **Atualizar `UpsertAsync` em `AiConfigService.cs`**

Substituir o método `UpsertAsync` completo por:

```csharp
public async Task<Result<AiConfig>> UpsertAsync(string schemaName, AiConfigRequest request)
{
    if (!ValidProviders.Contains(request.Provider))
        return Result<AiConfig>.Fail("Provider inválido. Use 'openai', 'anthropic' ou 'mock'.");
    if (string.IsNullOrWhiteSpace(request.SystemPrompt))
        return Result<AiConfig>.Fail("SystemPrompt é obrigatório.");

    await using var db = dbFactory.Create(schemaName);

    var existing = await db.AiConfigs.FirstOrDefaultAsync();
    if (existing is not null)
    {
        existing.Provider = request.Provider;
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
            existing.ApiKeyEncrypted = AesEncryption.Encrypt(request.ApiKey, encryptionKey);
        existing.Model = request.Model;
        existing.SystemPrompt = request.SystemPrompt;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Result<AiConfig>.Ok(existing);
    }

    if (string.IsNullOrWhiteSpace(request.ApiKey))
        return Result<AiConfig>.Fail("ApiKey é obrigatória para nova configuração.");

    var config = new AiConfig
    {
        Provider = request.Provider,
        ApiKeyEncrypted = AesEncryption.Encrypt(request.ApiKey, encryptionKey),
        Model = request.Model,
        SystemPrompt = request.SystemPrompt
    };
    db.AiConfigs.Add(config);
    await db.SaveChangesAsync();
    return Result<AiConfig>.Ok(config);
}
```

- [ ] **Build**

```powershell
dotnet build src/Atendefy.API/Atendefy.API.csproj
```

Esperado: 0 erros.

- [ ] **Testar manualmente**

```powershell
$token = (curl.exe -s -X POST "http://localhost:8080/auth/login" `
  -H "Content-Type: application/json" -H "X-Tenant-Key: jel" `
  -d '{"email":"josedudev@gmail.com","password":"Test@123"}' | ConvertFrom-Json).accessToken

# Salvar sem ApiKey (deve manter a chave existente)
curl.exe -s -X PUT "http://localhost:8080/ai/config" `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer $token" `
  -d '{"provider":"mock","model":"mock","systemPrompt":"Você é um assistente."}'
```

Esperado: `{"provider":"mock","model":"mock","systemPrompt":"Você é um assistente."}` (sem erro de ApiKey).

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/AI/Models/AiConfigRequest.cs `
      src/Atendefy.API/Modules/AI/AiConfigService.cs
git commit -m "fix: make ApiKey optional in AI config update"
```

---

### Task 2: WhatsApp connect/status (backend)

**Files:**
- Modify: `src/Atendefy.API/Modules/WhatsApp/WhatsAppAccountService.cs`
- Modify: `src/Atendefy.API/Modules/WhatsApp/WhatsAppEndpoints.cs`

Contexto: a Evolution API usa header `apikey: <value>` em todas as chamadas. O `ConfigJson` da conta contém `{ "base_url": "...", "instance": "...", "api_key": "..." }`. O endpoint `GET /instance/connectionState/{instance}` retorna `{"instance":{"instanceName":"...","state":"open"|"connecting"|"close"}}`. O `GET /instance/connect/{instance}` retorna `{"base64":"data:image/png;base64,...","code":"...","count":0}`. Se a instância não existe, retorna 404 — nesse caso criamos via `POST /instance/create`.

- [ ] **Adicionar `IHttpClientFactory`, `ConnectAsync` e `GetStatusAsync` a `WhatsAppAccountService.cs`**

Substituir o arquivo completo por:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Webhooks.Models;
using Atendefy.API.Modules.WhatsApp.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;

namespace Atendefy.API.Modules.WhatsApp;

public class WhatsAppAccountService(
    PublicDbContext publicDb,
    TenantDbContextFactory tenantDbFactory,
    IHttpClientFactory httpClientFactory)
{
    private static readonly HashSet<string> ValidProviders = ["meta", "evolution"];

    public async Task<Result<WhatsAppAccount>> CreateAsync(
        Guid tenantId, string schemaName, CreateAccountRequest request)
    {
        if (!ValidProviders.Contains(request.Provider))
            return Result<WhatsAppAccount>.Fail("Provider inválido. Use 'meta' ou 'evolution'.");

        if (string.IsNullOrWhiteSpace(request.ConfigJson))
            return Result<WhatsAppAccount>.Fail("ConfigJson é obrigatório.");

        await using var db = tenantDbFactory.Create(schemaName);

        var account = new WhatsAppAccount
        {
            Provider = request.Provider,
            Phone = request.Phone,
            ConfigJson = request.ConfigJson,
            Status = "disconnected"
        };

        db.WhatsAppAccounts.Add(account);
        await db.SaveChangesAsync();

        var lookupKey = request.Provider == "evolution"
            ? account.Id.ToString("N")
            : ExtractMetaPhoneNumberId(request.ConfigJson);

        publicDb.WebhookRoutes.Add(new WebhookRoute
        {
            TenantId = tenantId,
            Provider = request.Provider,
            LookupKey = lookupKey,
            AccountId = account.Id
        });
        await publicDb.SaveChangesAsync();

        return Result<WhatsAppAccount>.Ok(account);
    }

    public async Task<List<WhatsAppAccount>> ListAsync(string schemaName)
    {
        await using var db = tenantDbFactory.Create(schemaName);
        return await db.WhatsAppAccounts.ToListAsync();
    }

    public async Task<Result<WhatsAppConnectResult>> ConnectAsync(string schemaName, Guid accountId)
    {
        await using var db = tenantDbFactory.Create(schemaName);
        var account = await db.WhatsAppAccounts.FindAsync(accountId);
        if (account is null) return Result<WhatsAppConnectResult>.Fail("Conta não encontrada.");
        if (account.Provider != "evolution")
            return Result<WhatsAppConnectResult>.Fail("Apenas contas Evolution suportam QR code.");

        var cfg = EvolutionConfig.FromJson(account.ConfigJson!);
        var client = CreateEvolutionClient(cfg.ApiKey);
        var baseUrl = cfg.BaseUrl.TrimEnd('/');

        // Check if already connected
        var stateResp = await client.GetAsync($"{baseUrl}/instance/connectionState/{cfg.Instance}");
        if (stateResp.IsSuccessStatusCode)
        {
            var stateJson = await stateResp.Content.ReadFromJsonAsync<JsonElement>();
            var state = stateJson.GetProperty("instance").GetProperty("state").GetString();
            if (state == "open")
            {
                account.Status = "connected";
                await db.SaveChangesAsync();
                return Result<WhatsAppConnectResult>.Ok(new WhatsAppConnectResult(null, "open"));
            }
        }

        // Get QR code; create instance if it doesn't exist
        var connectResp = await client.GetAsync($"{baseUrl}/instance/connect/{cfg.Instance}");
        if (!connectResp.IsSuccessStatusCode)
        {
            var createPayload = new { instanceName = cfg.Instance, integration = "WHATSAPP-BAILEYS" };
            await client.PostAsJsonAsync($"{baseUrl}/instance/create", createPayload);
            connectResp = await client.GetAsync($"{baseUrl}/instance/connect/{cfg.Instance}");
        }

        if (!connectResp.IsSuccessStatusCode)
            return Result<WhatsAppConnectResult>.Fail("Falha ao obter QR code da Evolution API.");

        var connectJson = await connectResp.Content.ReadFromJsonAsync<JsonElement>();
        var qrBase64 = connectJson.GetProperty("base64").GetString();

        account.Status = "connecting";
        await db.SaveChangesAsync();

        return Result<WhatsAppConnectResult>.Ok(new WhatsAppConnectResult(qrBase64, "connecting"));
    }

    public async Task<Result<string>> GetStatusAsync(string schemaName, Guid accountId)
    {
        await using var db = tenantDbFactory.Create(schemaName);
        var account = await db.WhatsAppAccounts.FindAsync(accountId);
        if (account is null) return Result<string>.Fail("Conta não encontrada.");
        if (account.Provider != "evolution")
            return Result<string>.Fail("Apenas contas Evolution suportam status.");

        var cfg = EvolutionConfig.FromJson(account.ConfigJson!);
        var client = CreateEvolutionClient(cfg.ApiKey);

        var resp = await client.GetAsync(
            $"{cfg.BaseUrl.TrimEnd('/')}/instance/connectionState/{cfg.Instance}");
        if (!resp.IsSuccessStatusCode)
            return Result<string>.Ok("close");

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var state = json.GetProperty("instance").GetProperty("state").GetString() ?? "close";

        if (state == "open" && account.Status != "connected")
        {
            account.Status = "connected";
            await db.SaveChangesAsync();
        }

        return Result<string>.Ok(state);
    }

    private HttpClient CreateEvolutionClient(string apiKey)
    {
        var client = httpClientFactory.CreateClient("whatsapp");
        client.DefaultRequestHeaders.Remove("apikey");
        client.DefaultRequestHeaders.Add("apikey", apiKey);
        return client;
    }

    private static string ExtractMetaPhoneNumberId(string configJson)
    {
        try { return MetaConfig.FromJson(configJson).PhoneNumberId; }
        catch { return Guid.NewGuid().ToString("N"); }
    }
}

public record WhatsAppConnectResult(string? QrCode, string Status);
```

- [ ] **Adicionar endpoints `POST /{id}/connect` e `GET /{id}/status` em `WhatsAppEndpoints.cs`**

Substituir o arquivo completo por:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.WhatsApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.WhatsApp;

public static class WhatsAppEndpoints
{
    public static IEndpointRouteBuilder MapWhatsAppEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/whatsapp/accounts")
            .WithTags("WhatsApp")
            .RequireAuthorization();

        group.MapPost("/", async (
            [FromBody] CreateAccountRequest request,
            WhatsAppAccountService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (tenantId, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var result = await service.CreateAsync(tenantId, schemaName, request);
            return result.IsSuccess
                ? Results.Created($"/whatsapp/accounts/{result.Value!.Id}",
                    new { result.Value.Id, result.Value.Provider, result.Value.Phone, result.Value.Status })
                : Results.BadRequest(new { error = result.Error });
        });

        group.MapGet("/", async (
            WhatsAppAccountService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var accounts = await service.ListAsync(schemaName);
            return Results.Ok(accounts.Select(a => new
            {
                a.Id, a.Provider, a.Phone, a.Status, a.CreatedAt
            }));
        });

        group.MapPost("/{id:guid}/connect", async (
            Guid id,
            WhatsAppAccountService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var result = await service.ConnectAsync(schemaName, id);
            return result.IsSuccess
                ? Results.Ok(new { qrCode = result.Value!.QrCode, status = result.Value.Status })
                : Results.BadRequest(new { error = result.Error });
        });

        group.MapGet("/{id:guid}/status", async (
            Guid id,
            WhatsAppAccountService service,
            PublicDbContext publicDb,
            HttpContext ctx) =>
        {
            var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
            if (error is not null) return Results.Json(new { error }, statusCode: 401);

            var result = await service.GetStatusAsync(schemaName, id);
            return result.IsSuccess
                ? Results.Ok(new { status = result.Value })
                : Results.BadRequest(new { error = result.Error });
        });

        return app;
    }

    private static async Task<(Guid TenantId, string SchemaName, string? Error)> ResolveTenantAsync(
        HttpContext ctx, PublicDbContext publicDb)
    {
        var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            return (Guid.Empty, string.Empty, "Token inválido");

        var tenant = await publicDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null)
            return (Guid.Empty, string.Empty, "Tenant não encontrado");

        return (tenantId, tenant.SchemaName, null);
    }
}
```

- [ ] **Build**

```powershell
dotnet build src/Atendefy.API/Atendefy.API.csproj
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.API/Modules/WhatsApp/WhatsAppAccountService.cs `
      src/Atendefy.API/Modules/WhatsApp/WhatsAppEndpoints.cs
git commit -m "feat: add WhatsApp QR code connect and status endpoints"
```

---

### Task 3: Frontend — tipos e hooks WhatsApp

**Files:**
- Modify: `src/Atendefy.Web/src/types/api.ts`
- Modify: `src/Atendefy.Web/src/hooks/useWhatsApp.ts`

- [ ] **Adicionar tipos em `src/Atendefy.Web/src/types/api.ts`**

No final do arquivo, adicionar:

```typescript
export interface WhatsAppConnectResponse {
  qrCode?: string;
  status: string;
}

export interface WhatsAppStatusResponse {
  status: string;
}
```

- [ ] **Adicionar hooks em `src/Atendefy.Web/src/hooks/useWhatsApp.ts`**

Substituir o arquivo completo por:

```typescript
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type {
  CreateWhatsAppAccountRequest,
  WhatsAppAccount,
  WhatsAppConnectResponse,
  WhatsAppStatusResponse,
} from '@/types/api';

export function useWhatsAppAccounts() {
  return useQuery({
    queryKey: ['whatsapp-accounts'],
    queryFn: () =>
      apiClient.get<WhatsAppAccount[]>('/whatsapp/accounts').then((r) => r.data),
  });
}

export function useCreateWhatsAppAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateWhatsAppAccountRequest) =>
      apiClient.post<WhatsAppAccount>('/whatsapp/accounts', req).then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['whatsapp-accounts'] }),
  });
}

export function useConnectWhatsApp() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient
        .post<WhatsAppConnectResponse>(`/whatsapp/accounts/${id}/connect`)
        .then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['whatsapp-accounts'] }),
  });
}

export function useWhatsAppStatus(id: string | null, enabled: boolean) {
  const queryClient = useQueryClient();
  return useQuery({
    queryKey: ['whatsapp-status', id],
    queryFn: () =>
      apiClient
        .get<WhatsAppStatusResponse>(`/whatsapp/accounts/${id}/status`)
        .then((r) => r.data),
    enabled: !!id && enabled,
    refetchInterval: (query) =>
      query.state.data?.status === 'open' ? false : 3000,
    select: (data) => {
      if (data.status === 'open') {
        queryClient.invalidateQueries({ queryKey: ['whatsapp-accounts'] });
      }
      return data;
    },
  });
}
```

- [ ] **TypeScript check**

```powershell
npx tsc --noEmit --project src/Atendefy.Web/tsconfig.json
```

Esperado: 0 erros.

- [ ] **Commit**

```powershell
git add src/Atendefy.Web/src/types/api.ts `
      src/Atendefy.Web/src/hooks/useWhatsApp.ts
git commit -m "feat: add WhatsApp connect/status hooks and types"
```

---

### Task 4: WhatsApp QR code UI

**Files:**
- Modify: `src/Atendefy.Web/src/pages/WhatsAppPage.tsx`

Contexto: cada card de conta Evolution ganha um botão "Conectar". Clicar abre um Dialog. O Dialog chama `useConnectWhatsApp.mutate(id)` ao abrir, mostra o QR code retornado, e faz polling via `useWhatsAppStatus` a cada 3s. Quando status vira "open", mostra "Conectado!" e invalida a lista. Outros providers (meta) não mostram o botão "Conectar".

- [ ] **Substituir `src/Atendefy.Web/src/pages/WhatsAppPage.tsx`**

```typescript
import { useState } from 'react';
import { useCreateWhatsAppAccount, useWhatsAppAccounts, useConnectWhatsApp, useWhatsAppStatus } from '@/hooks/useWhatsApp';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { Plus, QrCode } from 'lucide-react';

type Provider = 'meta' | 'evolution';

const CONFIG_PLACEHOLDER: Record<Provider, string> = {
  meta: JSON.stringify({ phoneNumberId: '1234567890', accessToken: 'EAAxxxxxxx' }, null, 2),
  evolution: JSON.stringify(
    { base_url: 'http://evolution-api:8080', instance: 'atendefy-dev', api_key: 'dev_evolution_key' },
    null,
    2
  ),
};

function statusVariant(status: string): 'default' | 'secondary' | 'outline' {
  if (status === 'connected' || status === 'open') return 'default';
  if (status === 'disconnected' || status === 'close') return 'secondary';
  return 'outline';
}

function statusLabel(status: string): string {
  if (status === 'connected' || status === 'open') return 'conectado';
  if (status === 'connecting') return 'conectando…';
  if (status === 'disconnected' || status === 'close') return 'desconectado';
  return status;
}

// ─── QR Code Dialog ───────────────────────────────────────────────────────────

function QrDialog({ accountId }: { accountId: string }) {
  const [open, setOpen] = useState(false);
  const [qrCode, setQrCode] = useState<string | null>(null);
  const connect = useConnectWhatsApp();
  const { data: statusData } = useWhatsAppStatus(accountId, open);

  const isConnected = statusData?.status === 'open';

  function handleOpen(value: boolean) {
    setOpen(value);
    if (value) {
      setQrCode(null);
      connect.mutate(accountId, {
        onSuccess: (data) => {
          if (data.qrCode) setQrCode(data.qrCode);
        },
      });
    }
  }

  return (
    <Dialog open={open} onOpenChange={handleOpen}>
      <DialogTrigger render={<Button size="sm" variant="outline" />}>
        <QrCode className="h-4 w-4 mr-1" />
        Conectar
      </DialogTrigger>
      <DialogContent className="max-w-sm text-center">
        <DialogHeader>
          <DialogTitle>Conectar WhatsApp</DialogTitle>
        </DialogHeader>

        {isConnected ? (
          <div className="py-6 space-y-2">
            <p className="text-2xl">✓</p>
            <p className="font-medium text-green-600">WhatsApp conectado!</p>
          </div>
        ) : connect.isPending ? (
          <p className="py-6 text-muted-foreground">Gerando QR code…</p>
        ) : connect.isError ? (
          <p className="py-6 text-sm text-destructive">Erro ao gerar QR code. Tente novamente.</p>
        ) : qrCode ? (
          <div className="space-y-3">
            <img src={qrCode} alt="QR Code WhatsApp" className="mx-auto w-56 h-56 rounded-lg border" />
            <p className="text-sm text-muted-foreground">
              Abra o WhatsApp → Aparelhos conectados → Conectar um aparelho
            </p>
            <p className="text-xs text-muted-foreground">Verificando conexão a cada 3s…</p>
          </div>
        ) : null}
      </DialogContent>
    </Dialog>
  );
}

// ─── Main Page ────────────────────────────────────────────────────────────────

export default function WhatsAppPage() {
  const { data: accounts, isLoading } = useWhatsAppAccounts();
  const createAccount = useCreateWhatsAppAccount();

  const [open, setOpen] = useState(false);
  const [provider, setProvider] = useState<Provider>('meta');
  const [phone, setPhone] = useState('');
  const [configJson, setConfigJson] = useState(CONFIG_PLACEHOLDER.meta);
  const [error, setError] = useState('');

  function handleProviderChange(v: string) {
    const p = v as Provider;
    setProvider(p);
    setConfigJson(CONFIG_PLACEHOLDER[p]);
  }

  async function handleCreate() {
    setError('');
    try {
      JSON.parse(configJson);
    } catch {
      setError('configJson inválido — verifique o JSON.');
      return;
    }
    try {
      await createAccount.mutateAsync({ provider, phone, configJson });
      setOpen(false);
      setPhone('');
      setConfigJson(CONFIG_PLACEHOLDER[provider]);
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Erro ao criar conta.';
      setError(msg);
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Contas WhatsApp</h1>
        <Dialog open={open} onOpenChange={setOpen}>
          <DialogTrigger render={<Button />}>
            <Plus className="h-4 w-4 mr-2" />
            Nova conta
          </DialogTrigger>
          <DialogContent className="max-w-lg">
            <DialogHeader>
              <DialogTitle>Conectar conta WhatsApp</DialogTitle>
            </DialogHeader>
            <div className="space-y-4">
              <div className="space-y-1">
                <Label>Provedor</Label>
                <Select value={provider} onValueChange={(v) => v && handleProviderChange(v)}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="meta">Meta (WhatsApp Cloud API)</SelectItem>
                    <SelectItem value="evolution">Evolution API</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1">
                <Label htmlFor="phone">Número (com DDI, ex: +5511999999999)</Label>
                <Input
                  id="phone"
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  placeholder="+5511999999999"
                />
              </div>
              <div className="space-y-1">
                <Label htmlFor="configJson">Configuração (JSON)</Label>
                <Textarea
                  id="configJson"
                  className="font-mono text-xs"
                  rows={6}
                  value={configJson}
                  onChange={(e) => setConfigJson(e.target.value)}
                />
              </div>
              {error && <p className="text-sm text-destructive">{error}</p>}
              <Button
                className="w-full"
                onClick={handleCreate}
                disabled={createAccount.isPending}
              >
                {createAccount.isPending ? 'Salvando…' : 'Salvar'}
              </Button>
            </div>
          </DialogContent>
        </Dialog>
      </div>

      {isLoading && <p className="text-muted-foreground">Carregando…</p>}

      {!isLoading && accounts?.length === 0 && (
        <p className="text-muted-foreground">Nenhuma conta conectada ainda.</p>
      )}

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {accounts?.map((acc) => (
          <Card key={acc.id}>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium capitalize">{acc.provider}</CardTitle>
              <Badge variant={statusVariant(acc.status)}>{statusLabel(acc.status)}</Badge>
            </CardHeader>
            <CardContent>
              <p className="text-sm">{acc.phone}</p>
              <p className="text-xs text-muted-foreground mt-1">
                {new Date(acc.createdAt).toLocaleDateString('pt-BR')}
              </p>
              {acc.provider === 'evolution' && acc.status !== 'connected' && acc.status !== 'open' && (
                <div className="mt-3">
                  <QrDialog accountId={acc.id} />
                </div>
              )}
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}
```

- [ ] **TypeScript check**

```powershell
npx tsc --noEmit --project src/Atendefy.Web/tsconfig.json
```

Esperado: 0 erros.

- [ ] **Build containers e testar**

```powershell
cd C:\Projetos\JEL\JEL\Atendefy\infra
docker compose build atendefy-api atendefy-web 2>&1 | Select-Object -Last 5
docker compose up -d atendefy-api atendefy-web 2>&1 | Select-Object -Last 5
```

Abrir `http://localhost:3000/whatsapp`. Na conta Evolution existente, deve aparecer o botão "Conectar". Clicar → dialog abre → QR code aparece → escanear com WhatsApp → dialog mostra "Conectado!".

- [ ] **Commit**

```powershell
git add src/Atendefy.Web/src/pages/WhatsAppPage.tsx
git commit -m "feat: WhatsApp QR code connect flow with polling"
```
