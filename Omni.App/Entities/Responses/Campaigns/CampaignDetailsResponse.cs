using Omni.App.Entities.Dtos.Campaigns;
using Omni.App.Entities.Models;

namespace Omni.App.Entities.Responses.Campaigns;

/// <summary>TemplateBody is the approved template text (with its {{n}} blanks) — shown on the details page as "Message sent".</summary>
public record CampaignDetailsResponse(CampaignResponse Campaign, CampaignStatsResponse Stats, IReadOnlyList<CampaignRecipientRow> Recipients, string? TemplateBody = null);
