using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Requests.Campaigns;

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
