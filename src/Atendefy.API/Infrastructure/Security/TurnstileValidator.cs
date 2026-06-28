using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Atendefy.API.Infrastructure.Security;

/// <summary>
/// Valida o token do Cloudflare Turnstile no servidor. Secret vazio ⇒ bypass (conveniência de dev);
/// em produção o secret é obrigatório.
/// </summary>
public class TurnstileValidator(IHttpClientFactory httpClientFactory, string secretKey)
{
    private const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(secretKey);

    public async Task<bool> IsValidAsync(string? token, string? remoteIp)
    {
        if (!IsEnabled) return true;                 // dev: sem secret, não bloqueia
        if (string.IsNullOrWhiteSpace(token)) return false;

        var client = httpClientFactory.CreateClient("turnstile");
        var form = new List<KeyValuePair<string, string>>
        {
            new("secret", secretKey),
            new("response", token),
        };
        if (!string.IsNullOrWhiteSpace(remoteIp))
            form.Add(new("remoteip", remoteIp));

        try
        {
            var resp = await client.PostAsync(VerifyUrl, new FormUrlEncodedContent(form));
            if (!resp.IsSuccessStatusCode) return false;
            var result = await resp.Content.ReadFromJsonAsync<SiteVerifyResponse>();
            return result?.Success ?? false;
        }
        catch
        {
            // Falha de rede ao validar: nega (fail-closed) — o cliente pode tentar de novo.
            return false;
        }
    }

    private record SiteVerifyResponse([property: JsonPropertyName("success")] bool Success);
}
