namespace PlasticSurgery.Entities.Responses.Campaigns;

/// <summary>Aggregate reporting — always computed live from CampaignRecipient rows (the source of
/// truth), never stored on Campaign itself. Replied is a best-effort signal: a recipient whose
/// conversation received an inbound whatsapp_customer message after this campaign's send.</summary>
public record CampaignStatsResponse(
    int TotalRecipients, int Pending, int Queued, int Sent, int Delivered, int Read, int Failed, int Replied, int Skipped, int Booked
);
