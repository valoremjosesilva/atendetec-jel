using Atendefy.API.Modules.WhatsApp.Models;
using Atendefy.API.SharedKernel.Extensions;
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
            // A Meta entrega o número do webhook sem o nono dígito (BR); o envio exige com ele.
            to = BrazilPhone.NormalizeForSending(message.ToPhone),
            type = "text",
            text = new { body = message.Text }
        };

        var response = await httpClient.PostAsJsonAsync(
            $"https://graph.facebook.com/v19.0/{config.PhoneNumberId}/messages",
            payload);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Meta Cloud API retornou {(int)response.StatusCode}: {error}");
        }
    }

    public async Task SendInteractiveAsync(string toPhone, InteractiveMessage message)
    {
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.AccessToken);

        var interactive = new Dictionary<string, object?>
        {
            ["type"] = message.Kind == InteractiveKind.List ? "list" : "button",
            ["body"] = new { text = Trunc(message.Body, 1024) }
        };

        if (!string.IsNullOrEmpty(message.Header))
            interactive["header"] = new { type = "text", text = Trunc(message.Header, 60) };
        if (!string.IsNullOrEmpty(message.Footer))
            interactive["footer"] = new { text = Trunc(message.Footer, 60) };

        if (message.Kind == InteractiveKind.List)
        {
            interactive["action"] = new
            {
                button = Trunc(message.ButtonText, 20),
                sections = new[]
                {
                    new
                    {
                        title = "Opções",
                        rows = message.Options.Take(10)
                            .Select(o => new { id = Trunc(o.Id, 200), title = Trunc(o.Title, 24) })
                            .ToArray()
                    }
                }
            };
        }
        else
        {
            interactive["action"] = new
            {
                buttons = message.Options.Take(3)
                    .Select(o => new { type = "reply", reply = new { id = Trunc(o.Id, 256), title = Trunc(o.Title, 20) } })
                    .ToArray()
            };
        }

        var payload = new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = BrazilPhone.NormalizeForSending(toPhone),
            ["type"] = "interactive",
            ["interactive"] = interactive
        };

        var response = await httpClient.PostAsJsonAsync(
            $"https://graph.facebook.com/v19.0/{config.PhoneNumberId}/messages",
            payload);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Meta Cloud API (interactive) retornou {(int)response.StatusCode}: {error}");
        }
    }

    private static string Trunc(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}
