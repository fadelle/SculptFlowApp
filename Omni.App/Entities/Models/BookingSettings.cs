namespace Omni.App.Entities.Models;

public class BookingSettings
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public int DefaultBookingDurationMinutes { get; set; } = 30;
    public int BufferMinutes { get; set; }
    public int MinimumBookingNoticeMinutes { get; set; } = 240;
    public int MaximumAdvanceBookingDays { get; set; } = 60;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
