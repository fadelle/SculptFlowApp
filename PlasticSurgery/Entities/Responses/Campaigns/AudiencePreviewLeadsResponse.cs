using PlasticSurgery.Entities.Dtos.Campaigns;

namespace PlasticSurgery.Entities.Responses.Campaigns;

/// <summary>GET /api/campaigns/audience-preview/leads — the "Preview leads" action. Same audience
/// resolution as AudiencePreviewResponse (ICampaignAudienceService.GetEligibleLeadsAsync — no second
/// filtering implementation), just returning the first few matches instead of only a count, so a
/// clinic can sanity-check who's actually in the audience before creating the campaign.</summary>
public record AudiencePreviewLeadsResponse(int MatchingLeads, IReadOnlyList<AudiencePreviewLeadRow> Leads);
