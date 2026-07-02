using System.Text.Json;
using Atendefy.API.Infrastructure.Cache;
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.AI.Models;
using Atendefy.API.SharedKernel;
using Atendefy.API.SharedKernel.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.AI;

public class AiConfigService(
    TenantDbContextFactory dbFactory,
    string encryptionKey,
    RedisService redis)
{
    private static readonly HashSet<string> ValidProviders = ["openai", "anthropic", "mock"];
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private static string CacheKey(string schemaName) => $"aiconfig:{schemaName}";

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
            await redis.DeleteAsync(CacheKey(schemaName));
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
        await redis.DeleteAsync(CacheKey(schemaName));
        return Result<AiConfig>.Ok(config);
    }

    public async Task<AiConfig?> GetAsync(string schemaName)
    {
        var cached = await redis.GetAsync(CacheKey(schemaName));
        if (cached is not null)
            return JsonSerializer.Deserialize<AiConfig>(cached);

        await using var db = dbFactory.Create(schemaName);
        var config = await db.AiConfigs.FirstOrDefaultAsync();
        if (config is not null)
            await redis.SetAsync(CacheKey(schemaName),
                JsonSerializer.Serialize(config), CacheTtl);
        return config;
    }
}
