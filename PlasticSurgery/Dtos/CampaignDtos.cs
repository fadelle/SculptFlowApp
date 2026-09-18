namespace PlasticSurgery.Dtos;

public record CampaignResponse(
    Guid Id,
    Guid ClinicId,
    string Name,
    string CampaignType,
    string Channel,
    Guid? WhatsAppTemplateId,
    string? TemplateName,
    string AudienceType,
    string? AudienceFilters,
    string Status,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

/// <summary>Aggregate reporting — always computed live from CampaignRecipient rows (the source of
/// truth), never stored on Campaign itself. Replied is a best-effort signal: a recipient whose
/// conversation received an inbound whatsapp_customer message after this campaign's send.</summary>
public record CampaignStatsResponse(
    int TotalRecipients, int Pending, int Queued, int Sent, int Delivered, int Read, int Failed, int Replied, int Skipped, int Booked
);

/// <summary>One row in the campaign list page.</summary>
public record CampaignListRow(
    Guid Id, string Name, string? TemplateName, string Status,
    int TotalRecipients, int Sent, int Delivered, int Failed,
    DateTimeOffset? ScheduledAt, DateTimeOffset CreatedAt
);

public record CampaignRecipientRow(
    Guid Id, Guid LeadId, string? LeadFullName, string PhoneNumber, string Status,
    string? SkipReason, string? FailureCode, string? FailureReason,
    DateTimeOffset? QueuedAt, DateTimeOffset? SentAt, DateTimeOffset? DeliveredAt, DateTimeOffset? ReadAt,
    DateTimeOffset? RepliedAt, DateTimeOffset? BookedAt, DateTimeOffset? FailedAt
);

public record CampaignDetailsResponse(CampaignResponse Campaign, CampaignStatsResponse Stats, IReadOnlyList<CampaignRecipientRow> Recipients);

/// <summary>
/// Creates a Campaign + one CampaignRecipient per lead (draft state — nothing is sent yet).
/// Recipients come from EXACTLY ONE of two sources: an explicit LeadIds list (manual/custom
/// selection, as before), or — when LeadIds is empty and AudienceType is given — the audience is
/// computed server-side via ICampaignAudienceService.GetEligibleLeadsAsync and snapshotted into
/// CampaignRecipient rows at creation time. AudienceType/AudienceFilters are always stored on the
/// Campaign either way, purely as a record of how the audience was chosen.
/// VariablesByLeadId maps a LeadId to the ordered {{1}}, {{2}}... values for that recipient; a
/// lead without an entry gets no body substitutions (fine for templates with no variables).
/// </summary>
public record CreateCampaignRequest(
    Guid ClinicId,
    string Name,
    Guid WhatsAppTemplateId,
    IReadOnlyList<Guid> LeadIds,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>>? VariablesByLeadId,
    DateTimeOffset? ScheduledAt,
    string? CampaignType = null,
    string? AudienceType = null,
    string? AudienceFilters = null
);

/// <summary>GET /api/campaigns/audience-preview — read-only, no side effects. Lets the Campaign
/// page show "Matching Leads: N" before the clinic commits to creating the campaign.</summary>
public record AudiencePreviewResponse(int MatchingLeads);

/// <summary>GET /api/campaigns/audience-preview/leads — the "Preview leads" action. Same audience
/// resolution as AudiencePreviewResponse (ICampaignAudienceService.GetEligibleLeadsAsync — no second
/// filtering implementation), just returning the first few matches instead of only a count, so a
/// clinic can sanity-check who's actually in the audience before creating the campaign.</summary>
public record AudiencePreviewLeadsResponse(int MatchingLeads, IReadOnlyList<AudiencePreviewLeadRow> Leads);

public record AudiencePreviewLeadRow(Guid Id, string? FullName, string? Phone);

/// <summary>Result of processing one bounded batch of a campaign's queued recipients — see
/// CampaignService.ProcessBatchAsync. The dashboard's "Send" action processes one batch
/// immediately; anything left over stays queued for a follow-up call (n8n schedule, or clicking
/// "Send" again) rather than blocking one HTTP request on a large recipient list.</summary>
public record ProcessCampaignBatchResult(int Processed, int Succeeded, int Failed, int RemainingQueued, bool CampaignCompleted);
