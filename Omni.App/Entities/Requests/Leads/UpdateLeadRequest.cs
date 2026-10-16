namespace Omni.App.Entities.Requests.Leads;

public record UpdateLeadRequest(
    Guid? ServiceId,
    string? FullName,
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Email,
    string? PreferredLanguage,
    string? City,
    string? DesiredTimeline,
    string? Notes,
    DateTimeOffset? NextFollowupAt
);
