namespace Atendefy.API.Modules.Billing.Gateways;

public interface IBillingGatewayFactory
{
    IBillingGateway Create(string provider);
}
