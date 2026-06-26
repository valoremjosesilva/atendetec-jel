using Atendefy.API.Modules.WhatsApp.Models;

namespace Atendefy.API.Modules.WhatsApp;

public interface IWhatsAppProvider
{
    Task SendMessageAsync(OutboundMessage message);

    /// <summary>
    /// Envia uma mensagem interativa (lista/botões). Implementação padrão: degrada para
    /// texto numerado via <see cref="SendMessageAsync"/>. Providers com suporte nativo
    /// (Meta Cloud) sobrescrevem.
    /// </summary>
    Task SendInteractiveAsync(string toPhone, InteractiveMessage message) =>
        SendMessageAsync(new OutboundMessage(toPhone, InteractiveText.Render(message)));
}
