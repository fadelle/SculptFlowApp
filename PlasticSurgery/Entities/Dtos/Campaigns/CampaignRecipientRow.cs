namespace PlasticSurgery.Entities.Dtos.Campaigns;

public record CampaignRecipientRow(
    Guid Id, Guid LeadId, string? LeadFullName, string PhoneNumber, string Status,
    string? SkipReason, string? FailureCode, string? FailureReason,
    DateTimeOffset? QueuedAt, DateTimeOffset? SentAt, DateTimeOffset? DeliveredAt, DateTimeOffset? ReadAt,
    DateTimeOffset? RepliedAt, DateTimeOffset? BookedAt, DateTimeOffset? FailedAt
);
