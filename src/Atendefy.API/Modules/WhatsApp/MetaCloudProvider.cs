using Atendefy.API.Modules.WhatsApp.Models;
using System.Net.Http.Json;

namespace Atendefy.API.Modules.WhatsApp;

public class MetaCloudProvider(HttpClient httpClient, MetaConfig config) : IWhatsAppProvider
{
    public async Task SendMessageAsync(OutboundMessage message)
    {
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.AccessToken);

        var payload = new
        {
            messaging_product = "whatsapp",
            to = message.ToPhone,
            type = "text",
            text = new { body = message.Text }
        };

        var response = await httpClient.PostAsJsonAsync(
            $"https://graph.facebook.com/v19.0/{config.PhoneNumberId}/messages",
            payload);

        response.EnsureSuccessStatusCode();
    }
}
