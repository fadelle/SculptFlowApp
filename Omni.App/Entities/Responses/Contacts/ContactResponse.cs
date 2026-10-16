namespace Omni.App.Entities.Responses.Contacts;

public record ContactResponse(
    Guid Id,
    Guid ClientId,
    Guid? ServiceId,
    string? ServiceName,
    string? FullName,
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Email,
    string? Source,
    string? SourceDetail,
    string? CampaignName,
    string? ExternalContactId,
    string StageKey,
    string Qualification,
    string? PreferredLanguage,
    string? City,
    string? DesiredTimeline,
    string? Notes,
    bool MarketingOptIn,
    DateTimeOffset? OptedOutAt,
    DateTimeOffset? LastContactAt,
    DateTimeOffset? NextFollowupAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
