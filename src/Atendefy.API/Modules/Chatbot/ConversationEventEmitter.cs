using System.Collections.Concurrent;
using System.Threading.Channels;
namespace Atendefy.API.Modules.Chatbot;

public class ConversationEventEmitter : IConversationEventEmitter
{
    private readonly ConcurrentDictionary<string, List<ChannelWriter<string>>> _subs = new();
    private readonly object _lock = new();

    public void Subscribe(string tenantId, ChannelWriter<string> writer)
    {
        lock (_lock) { _subs.GetOrAdd(tenantId, _ => new List<ChannelWriter<string>>()).Add(writer); }
    }

    public void Unsubscribe(string tenantId, ChannelWriter<string> writer)
    {
        lock (_lock)
        {
            if (_subs.TryGetValue(tenantId, out var list))
            {
                list.Remove(writer);
                if (list.Count == 0) _subs.TryRemove(tenantId, out _);
            }
        }
    }

    public void Emit(string tenantId, string data)
    {
        List<ChannelWriter<string>> snapshot;
        lock (_lock)
        {
            if (!_subs.TryGetValue(tenantId, out var list)) return;
            snapshot = list.ToList();
        }
        foreach (var writer in snapshot) writer.TryWrite(data);
    }
}
