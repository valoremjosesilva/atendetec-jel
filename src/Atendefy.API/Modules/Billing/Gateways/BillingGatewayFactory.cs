namespace Atendefy.API.Modules.Billing.Gateways;

public class BillingGatewayFactory(
    IHttpClientFactory httpClientFactory,
    string asaasApiKey,
    string asaasWebhookToken,
    bool asaasSandbox,
    string stripeSecretKey,
    string stripeWebhookSecret) : IBillingGatewayFactory
{
    public IBillingGateway Create(string provider) => provider switch
    {
        "asaas" => new AsaasGateway(
            httpClientFactory.CreateClient("billing"),
            asaasApiKey, asaasWebhookToken, asaasSandbox),
        "stripe" => new StripeGateway(
            httpClientFactory.CreateClient("billing"),
            stripeSecretKey, stripeWebhookSecret),
        _ => throw new ArgumentException($"Billing provider desconhecido: {provider}")
    };
}
