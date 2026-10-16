namespace Omni.App.Common.Enums;

/// <summary>The stage keys the code itself moves a lead to (keys of the first built-in business type, crm.pipeline_stages);
/// what a client may use is its own business type's stages (IBusinessTypeService). Milestone A S6 replaces these with
/// stage categories.</summary>
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
}
