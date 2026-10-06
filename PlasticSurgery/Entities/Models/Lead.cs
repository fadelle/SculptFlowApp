using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

public class Lead
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid? ProcedureId { get; set; }

    public string? FullName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public string? Source { get; set; }
    public string? SourceDetail { get; set; }
    public string? CampaignName { get; set; }
    public string? ExternalLeadId { get; set; }

    public string Status { get; set; } = LeadStatus.New;
    public string QualificationStatus { get; set; } = LeadQualificationStatus.Unknown;

    public string? PreferredLanguage { get; set; }
    public string? CountryCode { get; set; }
    public string? City { get; set; }
    public string? DesiredTimeline { get; set; }
    public string? Notes { get; set; }

    public Guid? AssignedStaffId { get; set; }

    public bool MarketingOptIn { get; set; } = true;
    public DateTimeOffset? OptedOutAt { get; set; }

    public DateTimeOffset? LastContactAt { get; set; }
    public DateTimeOffset? NextFollowupAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Clinic? Clinic { get; set; }
    public Procedure? Procedure { get; set; }
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<ProcedureBooking> ProcedureBookings { get; set; } = new List<ProcedureBooking>();
}
