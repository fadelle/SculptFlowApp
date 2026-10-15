using Microsoft.Extensions.Logging;
using PlasticSurgery.Entities.Dtos.Monitoring;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Contracts.Managers;

/// <summary>
/// Singleton, in memory. DatabaseLoggerProvider adds every captured warning/error here (no database write on the
/// caller's path); AppLogFlushJob drains it every few seconds into activity.app_logs. Bounded: beyond
/// AppLogLimits.BufferCapacity entries are dropped and counted, and at most MaxPerFingerprintPerMinute entries per
/// fingerprint per minute are kept (the rest of that minute becomes one summary entry with a repeat count).
/// </summary>
public interface IAppLogCollector
{
    /// <summary>The ErrorLog settings currently applied.</summary>
    AppLogCaptureSettings Settings { get; }

    /// <summary>Replaces the settings snapshot (AppLogFlushJob, from IConfigManager).</summary>
    void Apply(AppLogCaptureSettings settings);

    /// <summary>
    /// Whether an entry of this level from this logger would be kept: the error log is on, the level is at or above the
    /// minimum, the logger isn't excluded, and the caller isn't inside <see cref="Suppress"/>.
    /// </summary>
    bool ShouldCapture(LogLevel level, string category);

    /// <summary>Queues one entry, applying the per-fingerprint cap. Never throws, never blocks on I/O.</summary>
    void Add(AppLog entry);

    /// <summary>Puts back entries a failed write returned (no cap applied; beyond the buffer they are counted as dropped).</summary>
    void Requeue(IEnumerable<AppLog> entries);

    /// <summary>
    /// Returns up to <paramref name="max"/> waiting entries, oldest first. First closes every fingerprint minute that
    /// ended before <paramref name="now"/> (writing its summary entry) and adds the "entries dropped" entry when some
    /// were lost.
    /// </summary>
    List<AppLog> Drain(DateTimeOffset now, int max);

    /// <summary>Entries waiting to be written.</summary>
    int Pending { get; }

    /// <summary>
    /// Stops capture for the current async flow until disposed. The flush job writes inside it, so a failing write (or
    /// EF's own warnings about it) can't feed itself back into the log.
    /// </summary>
    IDisposable Suppress();
}
