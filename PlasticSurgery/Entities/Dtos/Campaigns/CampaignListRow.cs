namespace PlasticSurgery.Entities.Dtos.Campaigns;

/// <summary>One row in the campaign list page.</summary>
public record CampaignListRow(
    Guid Id, string Name, string? TemplateName, string Status,
    int TotalRecipients, int Sent, int Delivered, int Failed,
    DateTimeOffset? ScheduledAt, DateTimeOffset CreatedAt,
    int Read = 0, int Replied = 0,
    /// <summary>all_eligible | reactivation_no_consultation | custom — with AudienceFilters null for a hand-picked list.</summary>
    string? AudienceType = null, bool ManuallySelected = false
);
