using Omni.App.Common.Enums;

namespace Omni.App.Entities.Requests.Campaigns;

/// <summary>
/// Creates a Campaign + one CampaignRecipient per contact (draft state — nothing is sent yet).
/// Recipients come from EXACTLY ONE of two sources: an explicit ContactIds list (manual/custom
/// selection, as before), or — when ContactIds is empty and AudienceType is given — the audience is
/// computed server-side via ICampaignAudienceService.GetEligibleContactsAsync and snapshotted into
/// CampaignRecipient rows at creation time. AudienceType/AudienceFilters are always stored on the
/// Campaign either way, purely as a record of how the audience was chosen.
/// VariablesByContactId maps a ContactId to the ordered {{1}}, {{2}}... values for that recipient; a
/// contact without an entry gets no body substitutions (fine for templates with no variables).
/// </summary>
public record CreateCampaignRequest(
    Guid ClientId,
    string Name,
    Guid WhatsAppTemplateId,
    IReadOnlyList<Guid> ContactIds,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>>? VariablesByContactId,
    DateTimeOffset? ScheduledAt,
    string? CampaignType = null,
    string? AudienceType = null,
    string? AudienceFilters = null
);
