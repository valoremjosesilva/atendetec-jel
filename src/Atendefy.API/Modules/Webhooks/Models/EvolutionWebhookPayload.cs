using System.Text.Json.Serialization;

namespace Atendefy.API.Modules.Webhooks.Models;

public record EvolutionWebhookPayload(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("instance")] string Instance,
    [property: JsonPropertyName("data")] EvolutionData Data
);

public record EvolutionData(
    [property: JsonPropertyName("key")] EvolutionKey Key,
    [property: JsonPropertyName("message")] EvolutionMessage? Message,
    [property: JsonPropertyName("pushName")] string? PushName = null
);

public record EvolutionKey(
    [property: JsonPropertyName("remoteJid")] string RemoteJid,
    [property: JsonPropertyName("fromMe")] bool FromMe
);

public record EvolutionMessage(
    [property: JsonPropertyName("conversation")] string? Conversation
);
