using Atendefy.API.Modules.AI.Models;

namespace Atendefy.API.Modules.AI;

public interface IAIProvider
{
    Task<AICompletionResult> CompleteAsync(AICompletionRequest request);
}
