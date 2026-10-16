namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>Traces with a step since a moment, how many of them failed, and the latest failed step's time.</summary>
public record TraceCounts(long Traces, long FailedTraces, DateTimeOffset? LastFailedAt);