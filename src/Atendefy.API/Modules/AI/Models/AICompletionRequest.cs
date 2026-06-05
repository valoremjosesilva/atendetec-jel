namespace Atendefy.API.Modules.AI.Models;

public record AICompletionRequest(
    string SystemPrompt,
    IReadOnlyList<ChatMessage> Messages,
    string Model,
    int MaxTokens = 1000
);
