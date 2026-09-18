namespace PlasticSurgery.Data.Entities;

public class Appointment
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid LeadId { get; set; }
    public Guid? ProcedureId { get; set; }

    public string AppointmentType { get; set; } = "consultation";
    public string Status { get; set; } = AppointmentStatus.Booked;
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

    public Lead? Lead { get; set; }
    public Procedure? Procedure { get; set; }
    public ICollection<ProcedureBooking> ProcedureBookings { get; set; } = new List<ProcedureBooking>();
}

public static class AppointmentStatus
{
    public const string Booked = "booked";
    public const string Confirmed = "confirmed";
    public const string Attended = "attended";
    public const string NoShow = "no_show";
    public const string Canceled = "canceled";
    public const string Rescheduled = "rescheduled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Booked, Confirmed, Attended, NoShow, Canceled, Rescheduled
    };
}
