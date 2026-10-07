namespace PlasticSurgery.Entities.Models;

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
