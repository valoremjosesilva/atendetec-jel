namespace Atendefy.API.Modules.Billing.Models;

public record CreateSubscriptionRequest(
    Guid PlanId,
    string Provider,          // "asaas" | "stripe"
    string BillingType,       // "BOLETO" | "PIX" | "CREDIT_CARD"
    string BillingCycle,      // "monthly" | "yearly"
    string? CpfCnpj,           // CPF ou CNPJ (obrigatório para Asaas, opcional para Stripe)
    string? PaymentMethodId   // Stripe payment method ID (obrigatório para CREDIT_CARD)
);
