using Atendefy.API.Modules.Billing.Models;

namespace Atendefy.API.Modules.Billing.Gateways;

public record CreateChargeArgs(
    string CustomerExternalId,
    decimal Amount,
    string BillingType,     // BOLETO | PIX | CREDIT_CARD
    string Description,
    DateOnly DueDate,
    string? PaymentMethodId  // Stripe only
);

public record WebhookEvent(
    string ExternalId,
    bool IsPaid,
    bool IsOverdue,
    bool IsCancelled
);

public interface IBillingGateway
{
    Task<string> CreateCustomerAsync(string name, string email, string cpfCnpj);
    Task<BillingCharge> CreateChargeAsync(CreateChargeArgs args);
    Task CancelChargeAsync(string externalId);
    bool ValidateWebhook(byte[] payload, string headerValue);
    WebhookEvent? ParseWebhookEvent(string json);
}
