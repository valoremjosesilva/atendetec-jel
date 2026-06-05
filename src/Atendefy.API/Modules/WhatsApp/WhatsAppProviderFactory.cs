using Atendefy.API.Modules.WhatsApp.Models;

namespace Atendefy.API.Modules.WhatsApp;

public class WhatsAppProviderFactory(IHttpClientFactory httpClientFactory)
{
    public IWhatsAppProvider Create(string provider, string configJson)
    {
        return provider switch
        {
            "meta" => new MetaCloudProvider(
                httpClientFactory.CreateClient("whatsapp"),
                MetaConfig.FromJson(configJson)),
            "evolution" => new EvolutionProvider(
                httpClientFactory.CreateClient("whatsapp"),
                EvolutionConfig.FromJson(configJson)),
            _ => throw new ArgumentException($"Provider desconhecido: {provider}")
        };
    }
}
