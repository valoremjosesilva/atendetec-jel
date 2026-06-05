using Atendefy.API.Modules.WhatsApp.Models;

namespace Atendefy.API.Modules.WhatsApp;

public interface IWhatsAppProvider
{
    Task SendMessageAsync(OutboundMessage message);
}
