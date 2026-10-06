namespace PlasticSurgery.Entities.Responses.Campaigns;

/// <summary>GET /api/campaigns/audience-preview — read-only, no side effects. Lets the Campaign
/// page show "Matching Leads: N" before the clinic commits to creating the campaign.</summary>
public record AudiencePreviewResponse(int MatchingLeads);
