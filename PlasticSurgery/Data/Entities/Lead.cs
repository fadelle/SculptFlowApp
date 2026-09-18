namespace PlasticSurgery.Data.Entities;

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

/// <summary>Allowed values for Lead.Status — must match the CHECK constraint in Database/schema.sql.</summary>
public static class LeadStatus
{
    public const string New = "new";
    public const string Contacted = "contacted";
    public const string Qualified = "qualified";
    public const string ConsultationBooked = "consultation_booked";
    public const string ConsultationAttended = "consultation_attended";
    public const string NoShow = "no_show";
    public const string SurgeryBooked = "surgery_booked";
    public const string NotInterested = "not_interested";
    public const string NeedsHuman = "needs_human";
    public const string Lost = "lost";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        New, Contacted, Qualified, ConsultationBooked, ConsultationAttended,
        NoShow, SurgeryBooked, NotInterested, NeedsHuman, Lost
    };
}

/// <summary>Allowed values for Lead.QualificationStatus — must match schema.sql. Named
/// LeadQualificationStatus (not QualificationStatus) because a class can't share its name with
/// an instance property of the type it's used to initialize — Lead.QualificationStatus's own
/// default-value initializer needs to reference this unambiguously.</summary>
public static class LeadQualificationStatus
{
    public const string Unknown = "unknown";
    public const string Hot = "hot";
    public const string Warm = "warm";
    public const string Cold = "cold";
    public const string NeedsHuman = "needs_human";
    public const string MedicalQuestion = "medical_question";
    public const string Spam = "spam";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Unknown, Hot, Warm, Cold, NeedsHuman, MedicalQuestion, Spam
    };
}
