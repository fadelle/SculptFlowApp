namespace PlasticSurgery.Entities.Models;

/// <summary>One calendar n8n reported as available on a connected account — cached so the picker doesn't need a live
/// round trip on every page load. Replaced wholesale on connect / "refresh calendars".</summary>
public class CalendarIntegrationCalendar
{
    public Guid Id { get; set; }
    public Guid CalendarIntegrationId { get; set; }
    public string ExternalCalendarId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
