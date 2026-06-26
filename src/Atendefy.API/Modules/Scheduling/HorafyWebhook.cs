using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atendefy.API.Modules.Scheduling.Models;

namespace Atendefy.API.Modules.Scheduling;

/// <summary>
/// Webhook de entrada do Horafy (write-back): valida a assinatura HMAC e converte o envelope
/// (booking.created/confirmed/cancelled) em <see cref="Appointment"/>. Extração defensiva:
/// nunca lança no parse.
/// </summary>
public static class HorafyWebhook
{
    /// <summary>Confere <c>X-Horafy-Signature</c> = sha256=HMAC-SHA256(secret, corpo_bruto).</summary>
    public static bool VerifySignature(string secret, byte[] body, string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return false;
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        var expected = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signature));
    }

    public static Appointment? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("booking", out var b) || b.ValueKind != JsonValueKind.Object)
            return null;

        // Id da reserva no Horafy: estável entre eventos (created/confirmed/cancelled) → idempotência.
        var id = Str(b, "id");
        if (string.IsNullOrEmpty(id)) return null;

        var status = (Str(b, "status") ?? string.Empty).ToLowerInvariant() switch
        {
            "cancelled" => "cancelled",
            _ => "confirmed"
        };
        if (Str(root, "event") == "booking.cancelled") status = "cancelled";

        string? serviceName = null;
        if (b.TryGetProperty("services", out var svcs) && svcs.ValueKind == JsonValueKind.Array
            && svcs.GetArrayLength() > 0)
            serviceName = Str(svcs[0], "serviceName");

        var resourceName = Str(b, "resourceName");
        var title = !string.IsNullOrEmpty(serviceName)
            ? (string.IsNullOrEmpty(resourceName) ? serviceName : $"{serviceName} — {resourceName}")
            : (resourceName ?? "Agendamento");

        string? name = null, email = null, phone = null;
        if (b.TryGetProperty("customer", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            name = Str(c, "name");
            email = Str(c, "email");
            phone = Str(c, "phone");
        }

        return new Appointment
        {
            ExternalId = id,
            Title = title,
            StartTime = Date(b, "scheduledAt"),
            EndTime = Date(b, "endsAt"),
            AttendeeName = name,
            AttendeeEmail = email,
            AttendeePhone = phone,
            Status = status
        };
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? Date(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            && DateTime.TryParse(v.GetString(), out var d) ? d.ToUniversalTime() : null;
}
