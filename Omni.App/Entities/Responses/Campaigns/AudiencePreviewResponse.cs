namespace Omni.App.Entities.Responses.Campaigns;

/// <summary>GET /api/campaigns/audience-preview — read-only, no side effects. Lets the Campaign
/// page show "Matching Contacts: N" before the client commits to creating the campaign.</summary>
public record AudiencePreviewResponse(int MatchingContacts);
