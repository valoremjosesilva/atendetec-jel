using Atendefy.API.Infrastructure.Cache;

namespace Atendefy.API.Infrastructure.RateLimiting;

public class TenantRateLimiter(RedisService redis, int limit = 60)
{
    public Task<bool> IsAllowedAsync(string tenantId, string scope = "msg") =>
        IsAllowedAsync(tenantId, scope, limit);

    // Overload com limite por chamada (ex.: cadastro anônimo por IP usa um limite bem mais estrito
    // que o de mensagens). Mesma janela de 1 minuto.
    public async Task<bool> IsAllowedAsync(string key, string scope, int customLimit)
    {
        var minute = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var redisKey = $"ratelimit:{scope}:{key}:{minute}";

        await redis.IncrementWithTtlAsync(redisKey, TimeSpan.FromMinutes(2));
        var count = await redis.GetCounterAsync(redisKey);

        return count <= customLimit;
    }
}
