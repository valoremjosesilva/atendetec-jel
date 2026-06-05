using Atendefy.API.Modules.AI.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace Atendefy.API.Modules.AI;

public class AnthropicProvider(HttpClient httpClient, string apiKey) : IAIProvider
{
    public async Task<AICompletionResult> CompleteAsync(AICompletionRequest request)
    {
        httpClient.DefaultRequestHeaders.Remove("x-api-key");
        httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        httpClient.DefaultRequestHeaders.Remove("anthropic-version");
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var payload = new
        {
            model = request.Model,
            max_tokens = request.MaxTokens,
            system = request.SystemPrompt,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content })
        };

        var response = await httpClient.PostAsJsonAsync(
            "https://api.anthropic.com/v1/messages", payload);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var content = json.GetProperty("content")[0]
                          .GetProperty("text")
                          .GetString() ?? string.Empty;
        var tokens = json.GetProperty("usage").GetProperty("output_tokens").GetInt32();

        return new AICompletionResult(content, tokens);
    }
}
