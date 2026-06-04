using StackExchange.Redis;

namespace Atendefy.API.Infrastructure.Messaging;

public class RedisStreamService(IConnectionMultiplexer connection)
{
    private readonly IDatabase _db = connection.GetDatabase();

    public async Task PublishAsync(string stream, Dictionary<string, string> fields)
    {
        var entries = fields.Select(f => new NameValueEntry(f.Key, f.Value)).ToArray();
        await _db.StreamAddAsync(stream, entries);
    }

    public async Task<StreamEntry[]> ReadGroupAsync(string stream, string group, string consumer, int count = 10)
        => await _db.StreamReadGroupAsync(stream, group, consumer, ">", count);

    public async Task AcknowledgeAsync(string stream, string group, RedisValue messageId)
        => await _db.StreamAcknowledgeAsync(stream, group, messageId);

    public async Task EnsureConsumerGroupAsync(string stream, string group)
    {
        try
        {
            await _db.StreamCreateConsumerGroupAsync(stream, group, StreamPosition.Beginning);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // grupo já existe — esperado
        }
    }
}
