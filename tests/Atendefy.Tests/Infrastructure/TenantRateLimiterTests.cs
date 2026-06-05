using Atendefy.API.Infrastructure.Cache;
using Atendefy.API.Infrastructure.RateLimiting;
using FluentAssertions;
using NSubstitute;
using StackExchange.Redis;

namespace Atendefy.Tests.Infrastructure;

public class TenantRateLimiterTests
{
    private static (RedisService redis, IDatabase db) CreateRedis()
    {
        var db = Substitute.For<IDatabase>();
        var conn = Substitute.For<IConnectionMultiplexer>();
        conn.GetDatabase().Returns(db);
        return (new RedisService(conn), db);
    }

    [Fact]
    public async Task IsAllowedAsync_WhenUnderLimit_ShouldReturnTrue()
    {
        var (redis, db) = CreateRedis();

        // IncrementAsync → _db.StringIncrementAsync returns 1 (first call)
        db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>()).Returns(1L);

        // GetCounterAsync → _db.StringGetAsync returns 1
        db.StringGetAsync(Arg.Any<RedisKey>()).Returns((RedisValue)"1");

        // SetAsync (TTL key) → _db.StringSetAsync
        db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
            Arg.Any<When>(), Arg.Any<CommandFlags>()).Returns(true);

        var limiter = new TenantRateLimiter(redis, limit: 60);

        var result = await limiter.IsAllowedAsync("tenant_abc");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAllowedAsync_WhenOverLimit_ShouldReturnFalse()
    {
        var (redis, db) = CreateRedis();

        // IncrementAsync → _db.StringIncrementAsync returns 61
        db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>()).Returns(61L);

        // GetCounterAsync → _db.StringGetAsync returns 61
        db.StringGetAsync(Arg.Any<RedisKey>()).Returns((RedisValue)"61");

        var limiter = new TenantRateLimiter(redis, limit: 60);

        var result = await limiter.IsAllowedAsync("tenant_abc");

        result.Should().BeFalse();
    }
}
