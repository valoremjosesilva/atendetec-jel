using Atendefy.API.Infrastructure.Cache;
using FluentAssertions;
using NSubstitute;
using StackExchange.Redis;

namespace Atendefy.Tests.Infrastructure;

public class RedisServiceTests
{
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly RedisService _sut;

    public RedisServiceTests()
    {
        var connection = Substitute.For<IConnectionMultiplexer>();
        connection.GetDatabase().Returns(_db);
        _sut = new RedisService(connection);
    }

    [Fact]
    public async Task SetAsync_ShouldCallRedisWithCorrectArguments()
    {
        _db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>())
           .Returns(true);

        await _sut.SetAsync("key:test", "value", TimeSpan.FromMinutes(30));

        await _db.Received(1).StringSetAsync(
            "key:test", "value", TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task GetAsync_WhenKeyExists_ShouldReturnValue()
    {
        _db.StringGetAsync("key:test").Returns(new RedisValue("hello"));

        var result = await _sut.GetAsync("key:test");

        result.Should().Be("hello");
    }

    [Fact]
    public async Task GetAsync_WhenKeyMissing_ShouldReturnNull()
    {
        _db.StringGetAsync("key:missing").Returns(RedisValue.Null);

        var result = await _sut.GetAsync("key:missing");

        result.Should().BeNull();
    }
}
