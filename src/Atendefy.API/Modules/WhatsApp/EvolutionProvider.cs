using Atendefy.API.Modules.WhatsApp.Models;
using System.Net.Http.Json;

namespace Atendefy.API.Modules.WhatsApp;

public class EvolutionProvider(HttpClient httpClient, EvolutionConfig config) : IWhatsAppProvider
{
    public async Task SendMessageAsync(OutboundMessage message)
    {
        httpClient.DefaultRequestHeaders.Remove("apikey");
        httpClient.DefaultRequestHeaders.Add("apikey", config.ApiKey);

        var payload = new
        {
            number = message.ToPhone,
            text = message.Text
        };

        var response = await httpClient.PostAsJsonAsync(
            $"{config.BaseUrl.TrimEnd('/')}/message/sendText/{config.Instance}",
            payload);

        response.EnsureSuccessStatusCode();
    }
}
