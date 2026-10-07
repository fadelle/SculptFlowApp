namespace PlasticSurgery.Common.Enums;

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
