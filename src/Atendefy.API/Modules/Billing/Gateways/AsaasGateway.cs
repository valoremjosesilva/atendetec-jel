using Atendefy.API.Modules.Billing.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace Atendefy.API.Modules.Billing.Gateways;

public class AsaasGateway(HttpClient httpClient, string apiKey, string webhookToken, bool isSandbox = false)
    : IBillingGateway
{
    private string BaseUrl => isSandbox
        ? "https://sandbox.asaas.com/api/v3"
        : "https://api.asaas.com/v3";

    private void SetAuth()
    {
        httpClient.DefaultRequestHeaders.Remove("access_token");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("access_token", apiKey);
    }

    public async Task<string> CreateCustomerAsync(string name, string email, string cpfCnpj)
    {
        SetAuth();
        var payload = new { name, email, cpfCnpj };
        var response = await httpClient.PostAsJsonAsync($"{BaseUrl}/customers", payload);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetString()!;
    }

    public async Task<BillingCharge> CreateChargeAsync(CreateChargeArgs args)
    {
        SetAuth();
        var payload = new
        {
            customer = args.CustomerExternalId,
            billingType = args.BillingType,
            value = args.Amount,
            dueDate = args.DueDate.ToString("yyyy-MM-dd"),
            description = args.Description
        };
        var response = await httpClient.PostAsJsonAsync($"{BaseUrl}/payments", payload);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("id").GetString()!;

        string? boletoUrl = null, boletoBarcode = null, pixCopyPaste = null;

        if (json.TryGetProperty("bankSlipUrl", out var bsUrl))
            boletoUrl = bsUrl.GetString();

        if (json.TryGetProperty("identificationField", out var idField))
            boletoBarcode = idField.GetString();

        if (json.TryGetProperty("pixTransaction", out var pixTx) &&
            pixTx.TryGetProperty("qrCode", out var qr) &&
            qr.TryGetProperty("payload", out var pixPayload))
            pixCopyPaste = pixPayload.GetString();

        return new BillingCharge(id, boletoUrl, boletoBarcode, pixCopyPaste, ClientSecret: null);
    }

    public async Task CancelChargeAsync(string externalId)
    {
        SetAuth();
        var response = await httpClient.DeleteAsync($"{BaseUrl}/payments/{externalId}");
        response.EnsureSuccessStatusCode();
    }

    public bool ValidateWebhook(byte[] payload, string headerValue)
        => headerValue == webhookToken;

    public WebhookEvent? ParseWebhookEvent(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var evt = doc.RootElement.GetProperty("event").GetString() ?? "";
            var payment = doc.RootElement.GetProperty("payment");
            var id = payment.GetProperty("id").GetString()!;

            return new WebhookEvent(
                ExternalId: id,
                IsPaid: evt == "PAYMENT_RECEIVED",
                IsOverdue: evt == "PAYMENT_OVERDUE",
                IsCancelled: evt == "PAYMENT_DELETED"
            );
        }
        catch
        {
            return null;
        }
    }
}
