using Atendefy.API.Modules.Billing.Gateways;
using Atendefy.Tests.Helpers;
using FluentAssertions;
using System.Text;

namespace Atendefy.Tests.Billing;

public class AsaasGatewayTests
{
    [Fact]
    public async Task CreateCustomerAsync_ShouldPostToAsaasAndReturnId()
    {
        var handler = MockHttpMessageHandler.ReturnsJson("""{"id":"cus_abc123","name":"Empresa Teste"}""");
        var gateway = new AsaasGateway(new HttpClient(handler), "sk_sandbox_key", "webhook_token", isSandbox: true);

        var id = await gateway.CreateCustomerAsync("Empresa Teste", "empresa@teste.com", "12345678000190");

        id.Should().Be("cus_abc123");
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("/customers");
        handler.Requests[0].Headers.GetValues("access_token").First().Should().Be("sk_sandbox_key");
    }

    [Fact]
    public async Task CreateChargeAsync_Boleto_ShouldReturnBoletoData()
    {
        var response = """
            {
                "id": "pay_xyz789",
                "status": "PENDING",
                "bankSlipUrl": "https://asaas.com/b/pdf/pay_xyz789",
                "identificationField": "1234.5678 9012.3456"
            }
            """;
        var handler = MockHttpMessageHandler.ReturnsJson(response);
        var gateway = new AsaasGateway(new HttpClient(handler), "sk_sandbox_key", "webhook_token", isSandbox: true);

        var charge = await gateway.CreateChargeAsync(new CreateChargeArgs(
            "cus_abc123", 99.90m, "BOLETO", "Plano Starter - Mensal",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), null));

        charge.ExternalId.Should().Be("pay_xyz789");
        charge.BoletoUrl.Should().Be("https://asaas.com/b/pdf/pay_xyz789");
        charge.BoletoBarcode.Should().Be("1234.5678 9012.3456");
    }

    [Fact]
    public async Task CreateChargeAsync_Pix_ShouldReturnPixData()
    {
        var response = """
            {
                "id": "pay_pix001",
                "status": "PENDING",
                "pixTransaction": {
                    "qrCode": {
                        "payload": "00020126360014br.gov.bcb.pix...",
                        "encodedImage": "base64img"
                    }
                }
            }
            """;
        var handler = MockHttpMessageHandler.ReturnsJson(response);
        var gateway = new AsaasGateway(new HttpClient(handler), "sk_sandbox_key", "webhook_token", isSandbox: true);

        var charge = await gateway.CreateChargeAsync(new CreateChargeArgs(
            "cus_abc123", 99.90m, "PIX", "Plano Starter - Mensal",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), null));

        charge.ExternalId.Should().Be("pay_pix001");
        charge.PixCopyPaste.Should().Contain("00020126");
    }

    [Fact]
    public void ValidateWebhook_WithCorrectToken_ShouldReturnTrue()
    {
        var gateway = new AsaasGateway(new HttpClient(), "sk_key", "meu_token_secreto", isSandbox: true);
        var body = Encoding.UTF8.GetBytes("""{"event":"PAYMENT_RECEIVED"}""");

        gateway.ValidateWebhook(body, "meu_token_secreto").Should().BeTrue();
        gateway.ValidateWebhook(body, "token_errado").Should().BeFalse();
    }

    [Fact]
    public void ParseWebhookEvent_PaymentReceived_ShouldReturnPaid()
    {
        var gateway = new AsaasGateway(new HttpClient(), "sk", "tok", isSandbox: true);
        var json = """{"event":"PAYMENT_RECEIVED","payment":{"id":"pay_001","status":"RECEIVED"}}""";

        var evt = gateway.ParseWebhookEvent(json);

        evt.Should().NotBeNull();
        evt!.ExternalId.Should().Be("pay_001");
        evt.IsPaid.Should().BeTrue();
        evt.IsOverdue.Should().BeFalse();
    }

    [Fact]
    public void ParseWebhookEvent_PaymentOverdue_ShouldReturnOverdue()
    {
        var gateway = new AsaasGateway(new HttpClient(), "sk", "tok", isSandbox: true);
        var json = """{"event":"PAYMENT_OVERDUE","payment":{"id":"pay_002","status":"OVERDUE"}}""";

        var evt = gateway.ParseWebhookEvent(json);

        evt!.IsOverdue.Should().BeTrue();
        evt.IsPaid.Should().BeFalse();
    }
}
