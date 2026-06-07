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
            if (stateJson.TryGetProperty("instance", out var instanceEl) &&
                instanceEl.TryGetProperty("state", out var stateEl) &&
                stateEl.GetString() == "open")
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
            var createResp = await client.PostAsJsonAsync($"{baseUrl}/instance/create", createPayload);
            if (!createResp.IsSuccessStatusCode)
                return Result<WhatsAppConnectResult>.Fail("Falha ao criar instância na Evolution API.");
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
        var state = "close";
        if (json.TryGetProperty("instance", out var instEl) &&
            instEl.TryGetProperty("state", out var stEl))
            state = stEl.GetString() ?? "close";

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
