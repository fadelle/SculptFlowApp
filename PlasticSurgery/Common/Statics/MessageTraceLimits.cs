namespace PlasticSurgery.Common.Statics;

/// <summary>Fixed shape of the message trace (MessageTracer, MessageTraceFlushJob, activity.message_trace_steps).</summary>
public static class MessageTraceLimits
{
    /// <summary>Steps waiting in memory; beyond this new steps are dropped (counted in the flush job's log).</summary>
    public const int BufferCapacity = 10_000;

    /// <summary>How often the flush job writes the waiting steps.</summary>
    public const int FlushSeconds = 5;

    /// <summary>Most rows written in one insert.</summary>
    public const int BatchSize = 500;

    /// <summary>How often the flush job deletes steps past retention.</summary>
    public const int PurgeIntervalMinutes = 60;

    /// <summary>Longest detail JSON stored per step, and longest single text value in it.</summary>
    public const int MaxDetailLength = 2000;
    public const int MaxDetailValueLength = 300;

    /// <summary>The header n8n's HTTP nodes send with their execution id, and the longest one kept.</summary>
    public const string N8nExecutionHeader = "X-N8n-Execution-Id";
    public const int MaxExecutionIdLength = 100;

    // How long after a trace's last step its messages' delivery statuses and events still count as part of it.
    public const int EventWindowMinutes = 10;

    // Platform-admin reads.
    public const int DefaultReportHours = 24;
    public const int MaxReportHours = 24 * 365;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
}