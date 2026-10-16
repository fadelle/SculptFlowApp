namespace Omni.App.Common.Enums;

/// <summary>Closed set (DB CHECK ck_campaigns_audience_type). See CampaignAudienceService for how
/// each type resolves to an actual contact list.</summary>
public static class CampaignAudienceType
{
    /// <summary>Every reachable contact of the client (mandatory exclusions only — do_not_contact,
    /// missing phone, opted out).</summary>
    public const string AllEligible = "all_eligible";
    /// <summary>Old/inactive contacts with prior interest/activity who never reached a successfully
    /// booked or completed booking — see CampaignAudienceService.</summary>
    public const string ReactivationNoBooking = "reactivation_no_booking";
    /// <summary>Client-defined filters (AudienceFilters), or an explicit hand-picked contact list.</summary>
    public const string Custom = "custom";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        AllEligible, ReactivationNoBooking, Custom
    };
}
