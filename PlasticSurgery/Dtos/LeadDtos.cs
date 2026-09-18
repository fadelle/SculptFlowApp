namespace PlasticSurgery.Dtos;

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

public record UpdateLeadRequest(
    Guid? ProcedureId,
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

/// <summary>QualificationStatus and Source are both optional — null means "leave unchanged".
/// Source has no fixed enum (Lead.Source is free text, no CHECK constraint — see Data/Entities/Lead.cs),
/// so it isn't validated against a status list the way Status/QualificationStatus are.</summary>
public record UpdateLeadStatusRequest(
    string Status,
    string? QualificationStatus,
    string? Source = null
);

/// <summary>Body for the AI agent's update_lead tool (POST /api/ai/leads/{id}) — deliberately
/// narrower than UpdateLeadRequest: it excludes identity fields (FullName/Phone/Email), which come
/// from the lead's actual source (webhook/intake), not the AI's judgment. Only null fields are left
/// unchanged — same partial-update semantics as UpdateLeadRequest.</summary>
public record UpdateLeadContextRequest(
    Guid? ProcedureId,
    string? PreferredLanguage,
    string? City,
    string? DesiredTimeline,
    string? Notes,
    DateTimeOffset? NextFollowupAt,
    string? QualificationStatus
);
