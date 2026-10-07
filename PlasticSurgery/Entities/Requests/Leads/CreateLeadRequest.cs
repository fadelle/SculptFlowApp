namespace PlasticSurgery.Entities.Requests.Leads;

public record CreateLeadRequest(
    Guid ClinicId,
    Guid? ProcedureId,
    string? FullName,
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Email,
    string? Source,
    string? SourceDetail,
    string? CampaignName,
    string? ExternalLeadId,
    string? PreferredLanguage,
    string? City,
    string? DesiredTimeline,
    string? Notes,
    bool MarketingOptIn = true
);
