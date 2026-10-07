namespace PlasticSurgery.Entities.Dtos.Dashboard;

/// <summary>Page 2 — Interested People (leads list) row.</summary>
public record DashboardLeadRow(
    Guid Id,
    string? FullName,
    string? Phone,
    string? ProcedureName,
    string? Source,
    string Status,
    string QualificationStatus,
    DateTimeOffset? LastContactAt,
    DateTimeOffset? NextFollowupAt,
    DateTimeOffset CreatedAt
);
