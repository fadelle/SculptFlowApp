namespace Omni.App.Entities.Requests.Contacts;

public record CreateContactRequest(
    Guid ClientId,
    Guid? ServiceId,
    string? FullName,
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Email,
    string? Source,
    string? SourceDetail,
    string? CampaignName,
    string? ExternalContactId,
    string? PreferredLanguage,
    string? City,
    string? DesiredTimeline,
    string? Notes,
    bool MarketingOptIn = true
);
