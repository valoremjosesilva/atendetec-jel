using Atendefy.API.Modules.AI.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace Atendefy.API.Modules.AI;

public class OpenAIProvider(
    HttpClient httpClient,
    string apiKey,
    string baseUrl = "https://api.openai.com/v1/chat/completions") : IAIProvider
{
    public async Task<AICompletionResult> CompleteAsync(AICompletionRequest request)
    {
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var messages = new List<object>
        {
            new { role = "system", content = request.SystemPrompt }
        };
        messages.AddRange(request.Messages.Select(m => new { role = m.Role, content = m.Content }));

        var payload = new
        {
            model = request.Model,
            messages,
            max_tokens = request.MaxTokens
        };

        var response = await httpClient.PostAsJsonAsync(baseUrl, payload);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        string content = string.Empty;
        int tokens = 0;

        if (json.TryGetProperty("choices", out var choices)
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var contentEl))
        {
            content = contentEl.GetString() ?? string.Empty;
        }

        if (json.TryGetProperty("usage", out var usage)
            && usage.TryGetProperty("completion_tokens", out var tokensEl))
        {
            tokens = tokensEl.GetInt32();
        }

        return new AICompletionResult(content, tokens);
    }
}
