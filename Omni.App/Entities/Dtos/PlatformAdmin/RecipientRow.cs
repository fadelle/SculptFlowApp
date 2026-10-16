namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record RecipientRow(Guid Id, Guid ContactId, string ContactName, string PhoneNumber, string Status, string? SkipReason,
    string? FailureReason, DateTimeOffset? SentAt, DateTimeOffset? RepliedAt, DateTimeOffset? BookedAt);
