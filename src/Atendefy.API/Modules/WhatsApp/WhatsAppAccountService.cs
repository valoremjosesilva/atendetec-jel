using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Webhooks.Models;
using Atendefy.API.Modules.WhatsApp.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.WhatsApp;

public class WhatsAppAccountService(
    PublicDbContext publicDb,
    TenantDbContextFactory tenantDbFactory)
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
            Status = "connected"
        };

        db.WhatsAppAccounts.Add(account);
        await db.SaveChangesAsync();

        // Registrar rota de webhook
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

    private static string ExtractMetaPhoneNumberId(string configJson)
    {
        try { return MetaConfig.FromJson(configJson).PhoneNumberId; }
        catch { return Guid.NewGuid().ToString("N"); }
    }
}
