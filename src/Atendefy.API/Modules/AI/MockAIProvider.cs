using Atendefy.API.Modules.AI.Models;

namespace Atendefy.API.Modules.AI;

public class MockAIProvider : IAIProvider
{
    public Task<AICompletionResult> CompleteAsync(AICompletionRequest request)
    {
        var userMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
        var reply = $"[Mock] Recebi sua mensagem: \"{userMessage}\"";
        return Task.FromResult(new AICompletionResult(reply, reply.Length));
    }
}
