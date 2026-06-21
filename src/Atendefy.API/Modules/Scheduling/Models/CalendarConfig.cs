namespace Atendefy.API.Modules.Scheduling.Models;

public class CalendarConfig
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = "calcom";
    public string? BookingUrl { get; set; }
    public bool Enabled { get; set; }
    public string? Instructions { get; set; }
    // Token usado na URL do webhook do Cal.com (Fase 3 — write-back). Gerado ao ativar.
    public string? WebhookToken { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public record CalendarConfigRequest(
    string? BookingUrl,
    bool Enabled,
    string? Instructions,
    string? Provider
);
