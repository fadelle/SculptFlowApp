namespace PlasticSurgery.Entities.Responses.Leads;

public record LeadResponse(
    Guid Id,
    Guid ClinicId,
    Guid? ProcedureId,
    string? ProcedureName,
    string? FullName,
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Email,
    string? Source,
    string? SourceDetail,
    string? CampaignName,
    string? ExternalLeadId,
    string Status,
    string QualificationStatus,
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
