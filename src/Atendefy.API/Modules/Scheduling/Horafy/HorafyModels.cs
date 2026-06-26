namespace Atendefy.API.Modules.Scheduling.Horafy;

/// <summary>Dados de conexão (por tenant) com a API do Horafy. ApiKey já descriptografada.</summary>
public sealed record HorafyConnection(string BaseUrl, string TenantSlug, string ApiKey);

// ── DTOs de leitura do catálogo/disponibilidade ─────────────────────────────────
public sealed record HorafyService(Guid Id, string Name, int DurationMinutes, decimal Price, string? Category);

public sealed record HorafyResource(Guid Id, string Name, string? Specialty, string? Type);

// ── Criação de agendamento ──────────────────────────────────────────────────────
public sealed record HorafyCreateBooking(
    IReadOnlyList<Guid> ServiceIds,
    Guid ResourceId,
    DateTimeOffset ScheduledAt,
    string CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    string? Notes,
    string? ExternalId,
    string? Source);

public sealed record HorafyBookingResult(Guid BookingId, bool AlreadyExisted);

public sealed record HorafyTestResult(bool Ok, int ServicesCount, string? Error);
