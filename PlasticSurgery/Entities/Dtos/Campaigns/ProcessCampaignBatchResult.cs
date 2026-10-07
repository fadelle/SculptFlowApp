namespace PlasticSurgery.Entities.Dtos.Campaigns;

/// <summary>Result of processing one bounded batch of a campaign's queued recipients — see
/// CampaignService.ProcessBatchAsync. The dashboard's "Send" action processes one batch
/// immediately; anything left over stays queued for a follow-up call (n8n schedule, or clicking
/// "Send" again) rather than blocking one HTTP request on a large recipient list.</summary>
public record ProcessCampaignBatchResult(int Processed, int Succeeded, int Failed, int RemainingQueued, bool CampaignCompleted);
