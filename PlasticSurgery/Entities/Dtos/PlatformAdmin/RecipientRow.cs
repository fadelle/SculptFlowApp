namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record RecipientRow(Guid Id, Guid LeadId, string LeadName, string PhoneNumber, string Status, string? SkipReason,
    string? FailureReason, DateTimeOffset? SentAt, DateTimeOffset? RepliedAt, DateTimeOffset? BookedAt);
