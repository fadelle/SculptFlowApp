using Omni.App.Entities.Dtos.Campaigns;

namespace Omni.App.Entities.Responses.Campaigns;

/// <summary>GET /api/campaigns/audience-preview/contacts — the "Preview contacts" action. Same audience
/// resolution as AudiencePreviewResponse (ICampaignAudienceService.GetEligibleContactsAsync — no second
/// filtering implementation), just returning the first few matches instead of only a count, so a
/// client can sanity-check who's actually in the audience before creating the campaign.</summary>
public record AudiencePreviewContactsResponse(int MatchingContacts, IReadOnlyList<AudiencePreviewContactRow> Contacts);
