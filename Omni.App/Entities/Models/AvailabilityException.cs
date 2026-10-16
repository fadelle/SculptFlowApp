namespace Omni.App.Entities.Models;

/// <summary>Overrides the weekly schedule for one local date: closed all day, or a custom window.</summary>
public class AvailabilityException
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public DateOnly Date { get; set; }
    public bool IsClosed { get; set; } = true;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
