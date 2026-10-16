namespace Omni.App.Entities.Dtos.Dashboard;

/// <summary>Page 2 — Interested People (contacts list) row.</summary>
public record DashboardContactRow(
    Guid Id,
    string? FullName,
    string? Phone,
    string? ServiceName,
    string? Source,
    string StageKey,
    string Qualification,
    DateTimeOffset? LastContactAt,
    DateTimeOffset? NextFollowupAt,
    DateTimeOffset CreatedAt
);
