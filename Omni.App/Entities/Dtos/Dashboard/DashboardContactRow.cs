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
    DateTimeOffset CreatedAt,
    // For the CSV export (QA t12-fix-05).
    string? Email = null,
    string? City = null,
    string? PreferredLanguage = null,
    DateTimeOffset? OptedOutAt = null
);
