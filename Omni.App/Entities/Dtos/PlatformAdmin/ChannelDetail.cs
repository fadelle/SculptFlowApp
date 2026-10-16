namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>One messaging connection for the admin portal. Secrets never leave the main app: only whether an access
/// token is stored.</summary>
public record ChannelDetail(Guid Id, Guid ClientId, NamedRef? Client, string Channel, string Status, string? DisplayName,
    string? PhoneNumberId, string? WhatsAppBusinessId, string? PageId, string? InstagramBusinessId, bool HasAccessToken,
    string? TelegramBotId, string? TelegramBotUsername, string? WebhookStatus, DateTimeOffset? WebhookRegisteredAt,
    DateTimeOffset? LastVerifiedAt, string? LastError, string? MetaBusinessId, string? VerifiedName,
    string? AccountStatus, string? AccountReviewStatus, string? PhoneQualityRating, string? PhoneStatus, string? NameStatus,
    bool IsHealthy, string? HealthLevel, string? LastProblemCode, string? LastProblemMessage, DateTimeOffset? LastWebhookAt,
    DateTimeOffset? LastHealthEventAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
