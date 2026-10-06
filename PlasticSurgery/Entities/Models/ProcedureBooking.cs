using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

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
