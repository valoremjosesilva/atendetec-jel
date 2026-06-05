using Atendefy.API.Infrastructure.Cache;

namespace Atendefy.API.Infrastructure.RateLimiting;

public class TenantRateLimiter(RedisService redis, int limit = 60)
{
    public async Task<bool> IsAllowedAsync(string tenantId)
    {
        var minute = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var key = $"ratelimit:{tenantId}:{minute}";

        await redis.IncrementAsync(key);
        var count = await redis.GetCounterAsync(key);

        if (count == 1)
            await redis.SetAsync($"{key}:ttl", "1", TimeSpan.FromMinutes(2));

        return count <= limit;
    }
}
