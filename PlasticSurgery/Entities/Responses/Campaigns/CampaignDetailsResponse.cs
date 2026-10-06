using PlasticSurgery.Entities.Dtos.Campaigns;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Entities.Responses.Campaigns;

/// <summary>TemplateBody is the approved template text (with its {{n}} blanks) — shown on the details page as "Message sent".</summary>
public record CampaignDetailsResponse(CampaignResponse Campaign, CampaignStatsResponse Stats, IReadOnlyList<CampaignRecipientRow> Recipients, string? TemplateBody = null);
