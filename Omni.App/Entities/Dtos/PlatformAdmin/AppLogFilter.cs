namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>
/// Which activity.app_logs rows a platform-admin read covers: logged at or after <see cref="From"/>, at one of
/// <see cref="Levels"/>, optionally one client, a text search (message, category or exception type, case-insensitive),
/// one fingerprint or one trace id.
/// </summary>
public record AppLogFilter(
    DateTimeOffset From,
    IReadOnlyList<string> Levels,
    Guid? ClientId = null,
    string? Search = null,
    string? Fingerprint = null,
    string? TraceId = null);