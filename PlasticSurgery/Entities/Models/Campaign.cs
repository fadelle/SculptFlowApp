using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

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
    public string CampaignType { get; set; } = Common.Enums.CampaignType.Custom;

    /// <summary>Delivery channel — see <see cref="CampaignChannel"/>. MVP only ever sends
    /// 'whatsapp'; the column allows 'instagram'/'messenger' values already (mirrors
    /// channel_integrations.channel) so no migration is needed when those ship.</summary>
    public string Channel { get; set; } = CampaignChannel.WhatsApp;

    /// <summary>Nullable at the DB level for forward-compatibility with a future non-template
    /// campaign type; every campaign actually sendable today still requires one (enforced in
    /// CampaignService.CreateAsync, not here).</summary>
    public Guid? WhatsAppTemplateId { get; set; }

    /// <summary>How the recipient list is determined — see <see cref="CampaignAudienceType"/>.</summary>
    public string AudienceType { get; set; } = CampaignAudienceType.Custom;

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
