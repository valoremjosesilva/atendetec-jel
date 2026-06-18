namespace Atendefy.API.Modules.AI;

public class AIProviderFactory(
    IHttpClientFactory httpClientFactory,
    string openAiBaseUrl = "https://api.openai.com/v1/chat/completions")
{
    public IAIProvider Create(string provider, string apiKey)
    {
        var client = httpClientFactory.CreateClient("ai");
        return provider switch
        {
            "openai" => new OpenAIProvider(client, apiKey, openAiBaseUrl),
            "anthropic" => new AnthropicProvider(client, apiKey),
            "mock" => new MockAIProvider(),
            _ => throw new ArgumentException($"Provider de IA desconhecido: {provider}")
        };
    }
}
