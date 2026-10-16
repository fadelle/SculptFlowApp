using Omni.App.Common.Enums;

namespace Omni.App.Entities.Models;

public class Booking
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid ContactId { get; set; }
    public Guid? ServiceId { get; set; }

    public string BookingType { get; set; } = "standard";
    public string Status { get; set; } = BookingStatus.Booked;
    public DateTimeOffset ScheduledStart { get; set; }
    public DateTimeOffset? ScheduledEnd { get; set; }
    public string? LocationType { get; set; }
    public string? LocationName { get; set; }
    public string? ExternalCalendarId { get; set; }
    public string? ExternalEventId { get; set; }
    public Guid? AssignedStaffId { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Contact? Contact { get; set; }
    public Service? Service { get; set; }
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
