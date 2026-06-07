namespace Atendefy.API.Modules.AI.Models;

public record AiConfigRequest(
    string Provider,
    string? ApiKey,
    string Model,
    string SystemPrompt
);
