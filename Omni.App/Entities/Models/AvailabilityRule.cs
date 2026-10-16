namespace Omni.App.Entities.Models;

/// <summary>One weekly opening window for a client. DayOfWeek uses .NET's DayOfWeek numbering (0 = Sunday); times are the
/// client's local wall-clock in Client.Timezone.</summary>
public class AvailabilityRule
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public int DayOfWeek { get; set; }
    public bool IsOpen { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
