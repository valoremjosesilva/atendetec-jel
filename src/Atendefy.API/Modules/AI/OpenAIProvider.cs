using Atendefy.API.Modules.AI.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace Atendefy.API.Modules.AI;

public class OpenAIProvider(HttpClient httpClient, string apiKey) : IAIProvider
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

        var response = await httpClient.PostAsJsonAsync(
            "https://api.openai.com/v1/chat/completions", payload);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var content = json.GetProperty("choices")[0]
                          .GetProperty("message")
                          .GetProperty("content")
                          .GetString() ?? string.Empty;
        var tokens = json.GetProperty("usage").GetProperty("completion_tokens").GetInt32();

        return new AICompletionResult(content, tokens);
    }
}
