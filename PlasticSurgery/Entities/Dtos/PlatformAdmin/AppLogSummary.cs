namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

/// <summary>
/// Counts for the admin portal's Overview tile: occurrences per level in the last <see cref="Hours"/> hours and when the
/// latest error or critical was logged.
/// </summary>
public record AppLogSummary(
    int Hours,
    DateTimeOffset From,
    bool Enabled,
    long Warnings,
    long Errors,
    long Criticals,
    DateTimeOffset? LastErrorAt);