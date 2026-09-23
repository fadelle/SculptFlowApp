namespace PlasticSurgery.Data.Entities;

/// <summary>One weekly opening window for a clinic. DayOfWeek uses .NET's DayOfWeek numbering (0 = Sunday); times are the
/// clinic's local wall-clock in Clinic.Timezone.</summary>
public class ClinicAvailabilityRule
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public int DayOfWeek { get; set; }
    public bool IsOpen { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class ClinicBookingSettings
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public int DefaultConsultationDurationMinutes { get; set; } = 30;
    public int BufferMinutes { get; set; }
    public int MinimumBookingNoticeMinutes { get; set; } = 240;
    public int MaximumAdvanceBookingDays { get; set; } = 60;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Overrides the weekly schedule for one local date: closed all day, or a custom window.</summary>
public class ClinicAvailabilityException
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public DateOnly Date { get; set; }
    public bool IsClosed { get; set; } = true;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
