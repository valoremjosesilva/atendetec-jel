namespace Atendefy.API.Modules.Scheduling.Models;

public class Appointment
{
    public Guid Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;  // uid do Cal.com (idempotência)
    public string? Title { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? AttendeeName { get; set; }
    public string? AttendeeEmail { get; set; }
    public string? AttendeePhone { get; set; }
    public string Status { get; set; } = "confirmed";  // confirmed | cancelled | rescheduled
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
