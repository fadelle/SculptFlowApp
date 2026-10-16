namespace Omni.App.Common.Statics;

/// <summary>Fixed shape of the error log (DatabaseLoggerProvider, AppLogCollector, AppLogFlushJob, activity.app_logs).</summary>
public static class AppLogLimits
{
    /// <summary>Entries waiting in memory; beyond this new entries are dropped and counted (one "dropped" row says how many).</summary>
    public const int BufferCapacity = 5000;

    /// <summary>How often the flush job writes the waiting entries (and picks up changed ErrorLog settings).</summary>
    public const int FlushSeconds = 5;

    /// <summary>Most rows written in one insert; the job keeps draining until the buffer is empty.</summary>
    public const int BatchSize = 500;

    /// <summary>How often the flush job deletes rows past retention.</summary>
    public const int PurgeIntervalMinutes = 60;

    /// <summary>Fingerprints tracked for the per-minute cap; beyond this, new fingerprints are written uncapped.</summary>
    public const int MaxTrackedFingerprints = 10_000;

    // Column sizes (activity.app_logs).
    public const int MaxMessageLength = 2000;
    public const int MaxTemplateLength = 1000;
    public const int MaxCategoryLength = 300;
    public const int MaxExceptionTypeLength = 300;
    public const int MaxExceptionMessageLength = 2000;
    public const int MaxStackLength = 8000;
    public const int MaxRouteLength = 300;
    public const int MaxInstanceLength = 100;

    /// <summary>Longest single property value kept; the whole properties object is capped at <see cref="MaxPropertiesLength"/>.</summary>
    public const int MaxPropertyValueLength = 500;
    public const int MaxPropertiesLength = 4000;

    /// <summary>This app instance's name in activity.app_logs.instance (the machine or container name).</summary>
    public static readonly string InstanceName =
        Environment.MachineName.Length <= MaxInstanceLength ? Environment.MachineName : Environment.MachineName[..MaxInstanceLength];

    /// <summary>Category of the row that reports entries dropped because the buffer was full.</summary>
    public const string DroppedCategory = "Omni.App.ErrorLog";

    // Platform-admin report.
    public const int DefaultReportHours = 24;
    public const int MaxReportHours = 24 * 365;
    public const int DefaultGroupTake = 100;
    public const int MaxGroupTake = 500;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
}
