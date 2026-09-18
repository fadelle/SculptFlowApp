namespace PlasticSurgery.Data.Entities;

public class ProcedureBooking
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid LeadId { get; set; }
    public Guid ProcedureId { get; set; }
    public Guid? AppointmentId { get; set; }

    public string Status { get; set; } = ProcedureBookingStatus.Considering;
    public decimal? QuotedAmount { get; set; }
    public decimal? DepositAmount { get; set; }
    public decimal? FinalAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTimeOffset? ProcedureDate { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Lead? Lead { get; set; }
    public Procedure? Procedure { get; set; }
    public Appointment? Appointment { get; set; }
}

public static class ProcedureBookingStatus
{
    public const string Considering = "considering";
    public const string Quoted = "quoted";
    public const string DepositPaid = "deposit_paid";
    public const string Booked = "booked";
    public const string Completed = "completed";
    public const string Canceled = "canceled";
    public const string Lost = "lost";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Considering, Quoted, DepositPaid, Booked, Completed, Canceled, Lost
    };
}
