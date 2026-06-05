namespace Atendefy.API.Modules.AI;

public class AIProviderFactory(IHttpClientFactory httpClientFactory)
{
    public IAIProvider Create(string provider, string apiKey)
    {
        var client = httpClientFactory.CreateClient("ai");
        return provider switch
        {
            "openai" => new OpenAIProvider(client, apiKey),
            "anthropic" => new AnthropicProvider(client, apiKey),
            _ => throw new ArgumentException($"Provider de IA desconhecido: {provider}")
        };
    }
}
