namespace PlasticSurgery.Entities.Models;

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
