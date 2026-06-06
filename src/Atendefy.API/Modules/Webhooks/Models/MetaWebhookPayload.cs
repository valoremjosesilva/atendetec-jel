using System.Text.Json.Serialization;

namespace Atendefy.API.Modules.Webhooks.Models;

public record MetaWebhookPayload(
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("entry")] List<MetaEntry> Entry
);

public record MetaEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("changes")] List<MetaChange> Changes
);

public record MetaChange(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("value")] MetaChangeValue Value
);

public record MetaChangeValue(
    [property: JsonPropertyName("metadata")] MetaMetadata Metadata,
    [property: JsonPropertyName("messages")] List<MetaMessage>? Messages
);

public record MetaMetadata(
    [property: JsonPropertyName("phone_number_id")] string PhoneNumberId
);

public record MetaMessage(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] MetaMessageText? Text
);

public record MetaMessageText(
    [property: JsonPropertyName("body")] string Body
);
