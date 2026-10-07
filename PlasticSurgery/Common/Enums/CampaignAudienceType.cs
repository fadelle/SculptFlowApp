namespace PlasticSurgery.Common.Enums;

/// <summary>Closed set (DB CHECK ck_campaigns_audience_type). See CampaignAudienceService for how
/// each type resolves to an actual lead list.</summary>
public static class CampaignAudienceType
{
    /// <summary>Every contactable lead of the clinic (mandatory exclusions only — do_not_contact,
    /// missing phone, opted out).</summary>
    public const string AllEligible = "all_eligible";
    /// <summary>Old/inactive leads with prior interest/activity who never reached a successfully
    /// booked or completed consultation — see CampaignAudienceService.</summary>
    public const string ReactivationNoConsultation = "reactivation_no_consultation";
    /// <summary>Clinic-defined filters (AudienceFilters), or an explicit hand-picked lead list.</summary>
    public const string Custom = "custom";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        AllEligible, ReactivationNoConsultation, Custom
    };
}
