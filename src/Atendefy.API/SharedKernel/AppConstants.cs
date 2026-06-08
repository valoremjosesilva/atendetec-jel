namespace Atendefy.API.SharedKernel;

public static class AppConstants
{
    public static class ConversationStatus
    {
        public const string Open = "open";
        public const string Resolved = "resolved";
        public const string All = "all";
    }

    public static class SubscriptionStatus
    {
        public const string Pending = "pending";
        public const string Active = "active";
        public const string PastDue = "past_due";
        public const string Suspended = "suspended";
        public const string Cancelled = "cancelled";
    }

    public static class TenantStatus
    {
        public const string Active = "active";
        public const string Suspended = "suspended";
        public const string Cancelled = "cancelled";
    }

    public static class InvoiceStatus
    {
        public const string Pending = "pending";
        public const string Paid = "paid";
        public const string Overdue = "overdue";
        public const string Cancelled = "cancelled";
    }

    public static class WhatsAppStatus
    {
        public const string Connected = "connected";
        public const string Open = "open";
        public const string Close = "close";
        public const string Connecting = "connecting";
        public const string Disconnected = "disconnected";
    }

    public static class MessageRole
    {
        public const string User = "user";
        public const string Assistant = "assistant";
        public const string Agent = "agent";
    }

    public static class WhatsAppProvider
    {
        public const string Meta = "meta";
        public const string Evolution = "evolution";
    }

    public static class BillingProvider
    {
        public const string Asaas = "asaas";
        public const string Stripe = "stripe";
    }

    public static class BillingType
    {
        public const string Boleto = "BOLETO";
        public const string Pix = "PIX";
        public const string CreditCard = "CREDIT_CARD";
    }

    public static class BillingCycle
    {
        public const string Monthly = "monthly";
        public const string Yearly = "yearly";
    }

    public static class AiProvider
    {
        public const string OpenAi = "openai";
        public const string Anthropic = "anthropic";
        public const string Mock = "mock";
    }
}
