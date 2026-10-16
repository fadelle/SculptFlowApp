namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>The dashboard tile: traces in the last <see cref="Hours"/> hours, how many had something fail, and when the
/// latest failure was.</summary>
public record MessageTraceSummary(int Hours, DateTimeOffset From, bool Enabled, long Traces, long FailedTraces, DateTimeOffset? LastFailedAt);