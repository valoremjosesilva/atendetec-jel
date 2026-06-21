using Atendefy.API.Modules.Scheduling.Models;
using System.Text.Json;

namespace Atendefy.API.Modules.Scheduling;

// Extração defensiva do payload de webhook do Cal.com — os campos variam conforme a
// configuração do event type, então tudo é best-effort e nunca lança.
public static class CalcomPayloadParser
{
    public static Appointment? Parse(JsonElement root)
    {
        var trigger = root.TryGetProperty("triggerEvent", out var t) ? t.GetString() : null;
        if (!root.TryGetProperty("payload", out var p)) return null;

        var uid = Str(p, "uid") ?? Str(p, "bookingId");
        if (string.IsNullOrEmpty(uid)) return null;

        var status = trigger switch
        {
            "BOOKING_CANCELLED" => "cancelled",
            "BOOKING_RESCHEDULED" => "rescheduled",
            _ => "confirmed"
        };

        string? name = null, email = null, phone = null;
        if (p.TryGetProperty("attendees", out var att) && att.ValueKind == JsonValueKind.Array
            && att.GetArrayLength() > 0)
        {
            var a0 = att[0];
            name = Str(a0, "name");
            email = Str(a0, "email");
            phone = Str(a0, "phoneNumber");
        }

        // Telefone também pode vir em responses.phone.value (pergunta customizada).
        if (string.IsNullOrEmpty(phone) && p.TryGetProperty("responses", out var resp)
            && resp.TryGetProperty("phone", out var ph) && ph.TryGetProperty("value", out var phv)
            && phv.ValueKind == JsonValueKind.String)
            phone = phv.GetString();

        return new Appointment
        {
            ExternalId = uid,
            Title = Str(p, "title"),
            StartTime = Date(p, "startTime"),
            EndTime = Date(p, "endTime"),
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
