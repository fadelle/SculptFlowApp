namespace Omni.App.Entities.Requests.Contacts;

public record UpdateContactRequest(
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
    DateTimeOffset? NextFollowupAt,
    /// <summary>True clears the next follow-up (NextFollowupAt null only means "unchanged").</summary>
    bool ClearNextFollowup = false
);
