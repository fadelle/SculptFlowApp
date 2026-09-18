namespace PlasticSurgery.Data.Entities;

/// <summary>
/// Sends one approved WhatsAppTemplate to many leads. A Campaign is purely orchestration/reporting
/// metadata — it never stores message content itself; every actual send still produces a normal
/// row in the existing messages table (see CampaignService.ProcessRecipientAsync), linked back via
/// Message.CampaignId/CampaignRecipientId.
/// </summary>
public class Campaign
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>What this campaign is for — see <see cref="CampaignType"/>. Deliberately a free
    /// string with no DB CHECK constraint (like Lead.Source) so new campaign types can be added
    /// without a migration.</summary>
    public string CampaignType { get; set; } = Entities.CampaignType.Custom;

    /// <summary>Delivery channel — see <see cref="CampaignChannel"/>. MVP only ever sends
    /// 'whatsapp'; the column allows 'instagram'/'messenger' values already (mirrors
    /// channel_integrations.channel) so no migration is needed when those ship.</summary>
    public string Channel { get; set; } = Entities.CampaignChannel.WhatsApp;

    /// <summary>Nullable at the DB level for forward-compatibility with a future non-template
    /// campaign type; every campaign actually sendable today still requires one (enforced in
    /// CampaignService.CreateAsync, not here).</summary>
    public Guid? WhatsAppTemplateId { get; set; }

    /// <summary>How the recipient list is determined — see <see cref="CampaignAudienceType"/>.</summary>
    public string AudienceType { get; set; } = Entities.CampaignAudienceType.Custom;

    /// <summary>Optional filters for AudienceType (inactiveDays, procedureId, leadStatuses,
    /// sources, ...) — see CampaignAudienceFilters. Raw JSON, not queried directly; a campaign
    /// audience is only ever computed once at creation time into CampaignRecipient rows (the
    /// snapshot), never re-derived from these filters afterward.</summary>
    public string? AudienceFilters { get; set; }

    public string Status { get; set; } = CampaignStatus.Draft;
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Clinic? Clinic { get; set; }
    public WhatsAppTemplate? WhatsAppTemplate { get; set; }
    public ICollection<CampaignRecipient> Recipients { get; set; } = new List<CampaignRecipient>();
}

public static class CampaignStatus
{
    public const string Draft = "draft";
    public const string Scheduled = "scheduled";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Draft, Scheduled, Running, Paused, Completed, Cancelled, Failed
    };
}

/// <summary>Extensible — no DB CHECK constraint (see Campaign.CampaignType).</summary>
public static class CampaignType
{
    public const string Reactivation = "reactivation";
    public const string FollowUp = "follow_up";
    public const string Promotion = "promotion";
    public const string Reminder = "reminder";
    public const string Custom = "custom";
}

/// <summary>Closed set (DB CHECK ck_campaigns_channel) — mirrors channel_integrations.channel.
/// Only WhatsApp is actually implemented; the others are reserved.</summary>
public static class CampaignChannel
{
    public const string WhatsApp = "whatsapp";
    public const string Instagram = "instagram";
    public const string Messenger = "messenger";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { WhatsApp, Instagram, Messenger };
}

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
