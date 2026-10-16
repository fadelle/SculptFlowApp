namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>Occurrences of one level since a moment (summed repeat counts) and when the latest was logged.</summary>
public record AppLogLevelCount(string Level, long Occurrences, DateTimeOffset LastAt);