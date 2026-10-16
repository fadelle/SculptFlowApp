using Microsoft.Extensions.Logging;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;

namespace Omni.App.Entities.Dtos.Monitoring;

/// <summary>
/// The ErrorLog settings the logging path needs, as one snapshot. AppLogFlushJob copies them from IConfigManager into
/// AppLogCollector on every flush; the logger reads only this copy, because the logging path can't depend on
/// ConfigManager (ConfigManager itself logs, which would loop, and it can't be built before logging is).
/// </summary>
public record AppLogCaptureSettings(
    bool Enabled,
    LogLevel MinimumLevel,
    IReadOnlyList<string> ExcludedCategoryPrefixes,
    int MaxPerFingerprintPerMinute)
{
    /// <summary>The ConfigDefaults values, used until the flush job's first copy.</summary>
    public static AppLogCaptureSettings Default { get; } = From(
        ConfigValues.ToBool(ConfigDefaults.ErrorLogEnabled.DefaultValue),
        ConfigDefaults.ErrorLogMinimumLevel.DefaultValue,
        ConfigDefaults.ErrorLogExcludedCategories.DefaultValue,
        ConfigValues.ToInt(ConfigDefaults.ErrorLogMaxPerFingerprintPerMinute.DefaultValue));

    /// <summary>Builds a snapshot from the raw setting values (an unknown level reads as Warning).</summary>
    public static AppLogCaptureSettings From(bool enabled, string? minimumLevel, string? excludedCategories, int maxPerFingerprintPerMinute) =>
        new(enabled,
            AppLogLevels.Parse(minimumLevel) ?? LogLevel.Warning,
            (excludedCategories ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            Math.Max(1, maxPerFingerprintPerMinute));
}
