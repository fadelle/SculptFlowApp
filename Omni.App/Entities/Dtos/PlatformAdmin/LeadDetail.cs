namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>One lead for the admin portal.</summary>
public record LeadDetail(Guid Id, Guid ClientId, NamedRef? Client, Guid? ProcedureId, NamedRef? Procedure, string? FullName,
    string? FirstName, string? LastName, string? Phone, string? Email, string? Source, string? SourceDetail, string? CampaignName,
    string? ExternalLeadId, string Status, string QualificationStatus, string? PreferredLanguage, string? CountryCode, string? City,
    string? DesiredTimeline, string? Notes, bool MarketingOptIn, DateTimeOffset? OptedOutAt, DateTimeOffset? LastContactAt,
    DateTimeOffset? NextFollowupAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
