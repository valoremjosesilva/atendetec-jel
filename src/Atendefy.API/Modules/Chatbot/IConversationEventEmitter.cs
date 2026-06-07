using System.Threading.Channels;
namespace Atendefy.API.Modules.Chatbot;

public interface IConversationEventEmitter
{
    void Subscribe(string tenantId, ChannelWriter<string> writer);
    void Unsubscribe(string tenantId, ChannelWriter<string> writer);
    void Emit(string tenantId, string data);
}
