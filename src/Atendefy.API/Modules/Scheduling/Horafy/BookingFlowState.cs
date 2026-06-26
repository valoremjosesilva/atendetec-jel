namespace Atendefy.API.Modules.Scheduling.Horafy;

/// <summary>Passos do fluxo guiado de agendamento.</summary>
public static class BookingStep
{
    public const string Service      = "service";
    public const string Professional = "professional";
    public const string Day          = "day";
    public const string Time         = "time";
    public const string Confirm      = "confirm";
}

/// <summary>Opção oferecida ao cliente (Id real + rótulo exibido).</summary>
public sealed class FlowOption
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// Estado serializável do fluxo de agendamento, por (tenant, telefone), guardado no Redis.
/// </summary>
public sealed class BookingFlowState
{
    public string Step { get; set; } = BookingStep.Service;

    public Guid? ServiceId { get; set; }
    public string? ServiceName { get; set; }
    public Guid? ResourceId { get; set; }
    public string? ResourceName { get; set; }
    public string? Date { get; set; }   // yyyy-MM-dd
    public string? Slot { get; set; }   // ISO-8601 (DateTimeOffset round-trip)

    /// <summary>Opções oferecidas no passo atual (para resolver a escolha do cliente).</summary>
    public List<FlowOption> Options { get; set; } = [];
}
