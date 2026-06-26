namespace Atendefy.API.Modules.Scheduling.Models;

public class CalendarConfig
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = "calcom";
    public string? BookingUrl { get; set; }
    public bool Enabled { get; set; }
    public string? Instructions { get; set; }
    // Token usado na URL do webhook (Cal.com/Horafy — write-back). Gerado ao ativar.
    public string? WebhookToken { get; set; }
    // Segredo (criptografado) para validar a assinatura HMAC dos webhooks do Horafy.
    public string? WebhookSecretEncrypted { get; set; }

    // ── Provider "horafy" (agenda própria via API) ──────────────────────────────
    /// <summary>Base URL da API do Horafy (ex.: https://barbearia.horafy.com.br).</summary>
    public string? ApiBaseUrl { get; set; }
    /// <summary>Slug do tenant no Horafy (header X-Tenant-Slug).</summary>
    public string? TenantSlug { get; set; }
    /// <summary>API key do Horafy, criptografada (AES). Trocada por JWT no token-exchange.</summary>
    public string? ApiKeyEncrypted { get; set; }
    /// <summary>Serviço padrão (opcional): pula o passo de escolha quando há só um.</summary>
    public Guid? DefaultServiceId { get; set; }
    /// <summary>Recurso/profissional padrão (opcional).</summary>
    public Guid? DefaultResourceId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public record CalendarConfigRequest(
    string? BookingUrl,
    bool Enabled,
    string? Instructions,
    string? Provider,
    // Campos do provider "horafy"
    string? ApiBaseUrl = null,
    string? TenantSlug = null,
    string? ApiKey = null,
    Guid? DefaultServiceId = null,
    Guid? DefaultResourceId = null,
    string? WebhookSecret = null
);
