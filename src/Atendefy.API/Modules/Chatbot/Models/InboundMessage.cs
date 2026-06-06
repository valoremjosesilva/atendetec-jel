namespace Atendefy.API.Modules.Chatbot.Models;

public record InboundMessage(
    string TenantId,
    string SchemaName,
    string ContactPhone,
    string MessageText,
    string Provider,
    string AccountId
);
