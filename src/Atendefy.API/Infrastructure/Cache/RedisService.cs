using StackExchange.Redis;

namespace Atendefy.API.Infrastructure.Cache;

public class RedisService(IConnectionMultiplexer connection)
{
    private readonly IDatabase _db = connection.GetDatabase();

    public async Task SetAsync(string key, string value, TimeSpan? expiry = null)
        => await _db.StringSetAsync(key, value, expiry);

    public async Task<string?> GetAsync(string key)
    {
        var value = await _db.StringGetAsync(key);
        return value.IsNull ? null : value.ToString();
    }

    public async Task DeleteAsync(string key)
        => await _db.KeyDeleteAsync(key);

    public async Task<bool> ExistsAsync(string key)
        => await _db.KeyExistsAsync(key);

    public async Task IncrementAsync(string key, long by = 1)
        => await _db.StringIncrementAsync(key, by);

    /// <summary>
    /// Incrementa atômico e devolve o novo valor. Define o TTL apenas na criação da chave
    /// (quando o novo valor == incremento), para não renovar a expiração a cada hit.
    /// </summary>
    public async Task<long> IncrementWithTtlAsync(string key, TimeSpan ttl, long by = 1)
    {
        var newValue = await _db.StringIncrementAsync(key, by);
        if (newValue == by) await _db.KeyExpireAsync(key, ttl);
        return newValue;
    }

    public async Task<long> GetCounterAsync(string key)
    {
        var value = await _db.StringGetAsync(key);
        return value.IsNull ? 0 : (long)value;
    }
}
