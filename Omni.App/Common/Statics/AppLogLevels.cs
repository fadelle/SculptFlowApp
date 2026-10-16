using Microsoft.Extensions.Logging;

namespace Omni.App.Common.Statics;

/// <summary>The levels stored in activity.app_logs.level, and how a level filter ("warning", "error", "critical") reads.</summary>
public static class AppLogLevels
{
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Critical = "Critical";

    public static readonly IReadOnlyList<string> All = [Warning, Error, Critical];

    /// <summary>The stored name for a log level at or above Warning; null for anything lower (never stored).</summary>
    public static string? NameOf(LogLevel level) => level switch
    {
        LogLevel.Warning => Warning,
        LogLevel.Error => Error,
        LogLevel.Critical => Critical,
        _ => null
    };

    /// <summary>"Warning", "Error" or "Critical" (any case) as a LogLevel; null when it's none of them.</summary>
    public static LogLevel? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "warning" => LogLevel.Warning,
        "error" => LogLevel.Error,
        "critical" => LogLevel.Critical,
        _ => null
    };

    /// <summary>The stored levels at or above <paramref name="minimum"/> (a minimum filter: "error" = Error + Critical).</summary>
    public static IReadOnlyList<string> AtOrAbove(LogLevel minimum) =>
        All.Where(name => Parse(name) >= minimum).ToList();
}
