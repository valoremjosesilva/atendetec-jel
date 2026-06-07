using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.AI.Models;
using Atendefy.API.SharedKernel;
using Atendefy.API.SharedKernel.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.AI;

public class AiConfigService(TenantDbContextFactory dbFactory, string encryptionKey)
{
    private static readonly HashSet<string> ValidProviders = ["openai", "anthropic", "mock"];

    public async Task<Result<AiConfig>> UpsertAsync(string schemaName, AiConfigRequest request)
    {
        if (!ValidProviders.Contains(request.Provider))
            return Result<AiConfig>.Fail("Provider inválido. Use 'openai' ou 'anthropic'.");
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return Result<AiConfig>.Fail("ApiKey é obrigatória.");
        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
            return Result<AiConfig>.Fail("SystemPrompt é obrigatório.");

        await using var db = dbFactory.Create(schemaName);

        var existing = await db.AiConfigs.FirstOrDefaultAsync();
        if (existing is not null)
        {
            existing.Provider = request.Provider;
            existing.ApiKeyEncrypted = AesEncryption.Encrypt(request.ApiKey, encryptionKey);
            existing.Model = request.Model;
            existing.SystemPrompt = request.SystemPrompt;
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Result<AiConfig>.Ok(existing);
        }

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

    public async Task<AiConfig?> GetAsync(string schemaName)
    {
        await using var db = dbFactory.Create(schemaName);
        return await db.AiConfigs.FirstOrDefaultAsync();
    }
}
