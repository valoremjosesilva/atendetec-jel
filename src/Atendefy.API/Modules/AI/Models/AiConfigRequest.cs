namespace Atendefy.API.Modules.AI.Models;

public record AiConfigRequest(
    string Provider,      // "openai" | "anthropic"
    string ApiKey,
    string Model,
    string SystemPrompt
);
